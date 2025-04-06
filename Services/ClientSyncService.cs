using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamCmdWeb.Services
{
    /// <summary>
    /// Dịch vụ xử lý đồng bộ âm thầm từ client
    /// </summary>
    public class SilentSyncService
    {
        private readonly ILogger<SilentSyncService> _logger;
        private readonly AppProfileManager _profileManager;
        private readonly string _syncFolder;
        private readonly object _syncStatusLock = new object();
        private SyncStatus _currentStatus = new SyncStatus();

        /// <summary>
        /// Khởi tạo dịch vụ SilentSync
        /// </summary>
        public SilentSyncService(ILogger<SilentSyncService> logger, AppProfileManager profileManager)
        {
            _logger = logger;
            _profileManager = profileManager;

            // Đảm bảo các thư mục tồn tại
            string baseDir = Directory.GetCurrentDirectory();
            _syncFolder = Path.Combine(baseDir, "Data", "SilentSync");
            if (!Directory.Exists(_syncFolder))
            {
                Directory.CreateDirectory(_syncFolder);
                _logger.LogInformation("Đã tạo thư mục SilentSync: {Path}", _syncFolder);
            }

            // Đảm bảo thư mục Backup tồn tại
            string backupFolder = Path.Combine(baseDir, "Data", "Backup");
            if (!Directory.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
                _logger.LogInformation("Đã tạo thư mục Backup: {Path}", backupFolder);
            }
            
            // Khởi tạo SyncStatus
            _currentStatus = new SyncStatus
            {
                LastSync = DateTime.MinValue,
                TotalSyncCount = 0,
                TotalProfilesReceived = 0,
                SuccessCount = 0,
                ErrorCount = 0,
                Status = "Ready"
            };
        }

        /// <summary>
        /// Xử lý nhận một profile từ client
        /// </summary>
        public async Task<(bool Success, string Message)> ReceiveProfileAsync(ClientProfile profile, string clientIp)
        {
            _logger.LogInformation("Nhận profile từ client {ClientIp}: {ProfileName} (ID: {ProfileId})",
                clientIp, profile.Name, profile.Id);

            try
            {
                if (profile == null)
                {
                    _logger.LogWarning("Nhận profile null từ client {ClientIp}", clientIp);
                    lock (_syncStatusLock)
                    {
                        _currentStatus.ErrorCount++;
                        _currentStatus.LastError = "Dữ liệu profile không hợp lệ";
                        _currentStatus.LastErrorTime = DateTime.Now;
                    }
                    return (false, "Dữ liệu profile không hợp lệ");
                }

                // Lưu backup
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"profile_{profile.Id}_{timestamp}.json";
                string filePath = Path.Combine(_syncFolder, fileName);

                string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                await File.WriteAllTextAsync(filePath, json);
                _logger.LogInformation("Đã lưu backup profile: {FilePath}", filePath);

                // Kiểm tra xem profile đã tồn tại trong DB chưa
                var existingProfile = _profileManager.GetProfileById(profile.Id);

                if (existingProfile == null)
                {
                    // Thêm profile mới
                    var result = _profileManager.AddProfile(profile);
                    _logger.LogInformation("Đã thêm profile mới: {ProfileName} (ID: {ProfileId})", result.Name, result.Id);
                    
                    lock (_syncStatusLock)
                    {
                        _currentStatus.LastSync = DateTime.Now;
                        _currentStatus.TotalSyncCount++;
                        _currentStatus.TotalProfilesReceived++;
                        _currentStatus.SuccessCount++;
                        _currentStatus.LastSuccess = $"Đã thêm profile {profile.Name} (ID: {profile.Id})";
                    }
                    
                    return (true, $"Đã thêm profile {profile.Name} (ID: {profile.Id})");
                }
                else
                {
                    // Cập nhật profile hiện có
                    bool updated = _profileManager.UpdateProfile(profile);
                    if (updated)
                    {
                        _logger.LogInformation("Đã cập nhật profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                        
                        lock (_syncStatusLock)
                        {
                            _currentStatus.LastSync = DateTime.Now;
                            _currentStatus.TotalSyncCount++;
                            _currentStatus.TotalProfilesReceived++;
                            _currentStatus.SuccessCount++;
                            _currentStatus.LastSuccess = $"Đã cập nhật profile {profile.Name} (ID: {profile.Id})";
                        }
                        
                        return (true, $"Đã cập nhật profile {profile.Name} (ID: {profile.Id})");
                    }
                    else
                    {
                        _logger.LogWarning("Không thể cập nhật profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                        
                        lock (_syncStatusLock)
                        {
                            _currentStatus.LastSync = DateTime.Now;
                            _currentStatus.TotalSyncCount++;
                            _currentStatus.ErrorCount++;
                            _currentStatus.LastError = $"Không thể cập nhật profile {profile.Name} (ID: {profile.Id})";
                            _currentStatus.LastErrorTime = DateTime.Now;
                        }
                        
                        return (false, $"Không thể cập nhật profile {profile.Name} (ID: {profile.Id})");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profile từ client {ClientIp}", clientIp);
                
                lock (_syncStatusLock)
                {
                    _currentStatus.LastSync = DateTime.Now;
                    _currentStatus.TotalSyncCount++;
                    _currentStatus.ErrorCount++;
                    _currentStatus.LastError = $"Lỗi xử lý profile: {ex.Message}";
                    _currentStatus.LastErrorTime = DateTime.Now;
                }
                
                return (false, $"Lỗi xử lý profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Xử lý nhận nhiều profile cùng lúc từ client
        /// </summary>
        public async Task<(bool Success, string Message, int Added, int Updated, int Failed, List<int> ProcessedIds)>
            ReceiveProfilesAsync(List<ClientProfile> profiles, string clientIp)
        {
            _logger.LogInformation("Nhận batch {Count} profiles từ client {ClientIp}", profiles?.Count ?? 0, clientIp);

            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Nhận batch profiles trống từ client {ClientIp}", clientIp);
                    
                    lock (_syncStatusLock)
                    {
                        _currentStatus.LastSync = DateTime.Now;
                        _currentStatus.TotalSyncCount++;
                        _currentStatus.ErrorCount++;
                        _currentStatus.LastError = "Không có profiles để xử lý";
                        _currentStatus.LastErrorTime = DateTime.Now;
                    }
                    
                    return (false, "Không có profiles để xử lý", 0, 0, 0, new List<int>());
                }

                // Lưu backup của toàn bộ batch
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"batch_{clientIp.Replace(":", "_")}_{timestamp}.json";
                string filePath = Path.Combine(_syncFolder, fileName);

                string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                await File.WriteAllTextAsync(filePath, json);
                _logger.LogInformation("Đã lưu backup batch profiles: {FilePath}", filePath);

                // Xử lý từng profile
                int addedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;
                List<int> processedIds = new List<int>();

                foreach (var profile in profiles)
                {
                    try
                    {
                        if (profile == null)
                        {
                            errorCount++;
                            continue;
                        }

                        var existingProfile = _profileManager.GetProfileById(profile.Id);

                        if (existingProfile == null)
                        {
                            // Thêm mới profile
                            var result = _profileManager.AddProfile(profile);
                            processedIds.Add(result.Id);
                            addedCount++;
                            _logger.LogInformation("Đã thêm profile mới: {ProfileName} (ID: {ProfileId})", result.Name, result.Id);
                        }
                        else
                        {
                            // Cập nhật profile hiện có
                            bool updated = _profileManager.UpdateProfile(profile);
                            if (updated)
                            {
                                processedIds.Add(profile.Id);
                                updatedCount++;
                                _logger.LogInformation("Đã cập nhật profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                            }
                            else
                            {
                                errorCount++;
                                _logger.LogWarning("Không thể cập nhật profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logger.LogError(ex, "Lỗi xử lý profile {ProfileId} trong batch", profile?.Id);
                    }
                }

                string resultMessage = $"Đã xử lý {addedCount + updatedCount} profiles (Thêm: {addedCount}, Cập nhật: {updatedCount}, Lỗi: {errorCount})";
                _logger.LogInformation("Batch complete: {Message}", resultMessage);
                
                lock (_syncStatusLock)
                {
                    _currentStatus.LastSync = DateTime.Now;
                    _currentStatus.TotalSyncCount++;
                    _currentStatus.TotalProfilesReceived += (addedCount + updatedCount);
                    _currentStatus.SuccessCount += (addedCount + updatedCount);
                    _currentStatus.ErrorCount += errorCount;
                    _currentStatus.LastSuccess = resultMessage;
                    _currentStatus.Status = "Active";
                }

                return (true, resultMessage, addedCount, updatedCount, errorCount, processedIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xử lý batch profiles từ client {ClientIp}", clientIp);
                
                lock (_syncStatusLock)
                {
                    _currentStatus.LastSync = DateTime.Now;
                    _currentStatus.TotalSyncCount++;
                    _currentStatus.ErrorCount++;
                    _currentStatus.LastError = $"Lỗi xử lý batch: {ex.Message}";
                    _currentStatus.LastErrorTime = DateTime.Now;
                }
                
                return (false, $"Lỗi xử lý batch: {ex.Message}", 0, 0, 0, new List<int>());
            }
        }

        /// <summary>
        /// Xử lý full silent sync từ client
        /// </summary>
        public async Task<(bool Success, string Message, int Total, int Added, int Updated, int Failed)>
            ProcessFullSyncAsync(string jsonData, string clientIp)
        {
            _logger.LogInformation("Nhận full silent sync từ client {ClientIp}, kích thước data: {Size}",
                clientIp, jsonData?.Length ?? 0);

            try
            {
                if (string.IsNullOrEmpty(jsonData))
                {
                    _logger.LogWarning("Nhận dữ liệu trống trong full sync từ client {ClientIp}", clientIp);
                    
                    lock (_syncStatusLock)
                    {
                        _currentStatus.LastSync = DateTime.Now;
                        _currentStatus.TotalSyncCount++;
                        _currentStatus.ErrorCount++;
                        _currentStatus.LastError = "Dữ liệu trống";
                        _currentStatus.LastErrorTime = DateTime.Now;
                    }
                    
                    return (false, "Dữ liệu trống", 0, 0, 0, 0);
                }

                // Lưu backup dữ liệu gốc
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"fullsync_{clientIp.Replace(":", "_")}_{timestamp}.json";
                string filePath = Path.Combine(_syncFolder, fileName);

                await File.WriteAllTextAsync(filePath, jsonData);
                _logger.LogInformation("Đã lưu backup full sync: {FilePath}", filePath);

                // Phân tích dữ liệu JSON
                List<ClientProfile> profiles;
                try
                {
                    profiles = JsonSerializer.Deserialize<List<ClientProfile>>(jsonData,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            AllowTrailingCommas = true,
                            ReadCommentHandling = JsonCommentHandling.Skip
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi phân tích JSON data từ client {ClientIp}", clientIp);
                    
                    lock (_syncStatusLock)
                    {
                        _currentStatus.LastSync = DateTime.Now;
                        _currentStatus.TotalSyncCount++;
                        _currentStatus.ErrorCount++;
                        _currentStatus.LastError = $"Lỗi phân tích dữ liệu JSON: {ex.Message}";
                        _currentStatus.LastErrorTime = DateTime.Now;
                    }
                    
                    return (false, $"Lỗi phân tích dữ liệu JSON: {ex.Message}", 0, 0, 0, 0);
                }

                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Không có profiles hợp lệ trong full sync từ client {ClientIp}", clientIp);
                    
                    lock (_syncStatusLock)
                    {
                        _currentStatus.LastSync = DateTime.Now;
                        _currentStatus.TotalSyncCount++;
                        _currentStatus.ErrorCount++;
                        _currentStatus.LastError = "Không có profiles hợp lệ trong dữ liệu";
                        _currentStatus.LastErrorTime = DateTime.Now;
                    }
                    
                    return (false, "Không có profiles hợp lệ trong dữ liệu", 0, 0, 0, 0);
                }

                // Xử lý từng profile
                int addedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;

                foreach (var profile in profiles)
                {
                    try
                    {
                        if (profile == null) continue;

                        var existingProfile = _profileManager.GetProfileById(profile.Id);

                        if (existingProfile == null)
                        {
                            // Thêm mới profile
                            _profileManager.AddProfile(profile);
                            addedCount++;
                            _logger.LogInformation("Đã thêm profile mới: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                        }
                        else
                        {
                            // Cập nhật profile hiện có
                            bool updated = _profileManager.UpdateProfile(profile);
                            if (updated)
                            {
                                updatedCount++;
                                _logger.LogInformation("Đã cập nhật profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                            }
                            else
                            {
                                errorCount++;
                                _logger.LogWarning("Không thể cập nhật profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logger.LogError(ex, "Lỗi xử lý profile {ProfileId} trong full sync", profile?.Id);
                    }
                }

                string resultMessage = $"Full sync thành công. Thêm: {addedCount}, Cập nhật: {updatedCount}, Lỗi: {errorCount}";
                _logger.LogInformation("Full sync complete: {Message}", resultMessage);
                
                lock (_syncStatusLock)
                {
                    _currentStatus.LastSync = DateTime.Now;
                    _currentStatus.TotalSyncCount++;
                    _currentStatus.TotalProfilesReceived += (addedCount + updatedCount);
                    _currentStatus.SuccessCount += (addedCount + updatedCount);
                    _currentStatus.ErrorCount += errorCount;
                    _currentStatus.LastSuccess = resultMessage;
                    _currentStatus.LastFullSync = DateTime.Now;
                    _currentStatus.Status = "Active";
                }

                return (true, resultMessage, profiles.Count, addedCount, updatedCount, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xử lý full sync từ client {ClientIp}", clientIp);
                
                lock (_syncStatusLock)
                {
                    _currentStatus.LastSync = DateTime.Now;
                    _currentStatus.TotalSyncCount++;
                    _currentStatus.ErrorCount++;
                    _currentStatus.LastError = $"Lỗi xử lý full sync: {ex.Message}";
                    _currentStatus.LastErrorTime = DateTime.Now;
                }
                
                return (false, $"Lỗi xử lý full sync: {ex.Message}", 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Lấy thông tin về các lần sync gần đây
        /// </summary>
        public object GetSyncStatus()
        {
            try
            {
                // Lấy thông tin về lần đồng bộ cuối
                var syncFiles = new DirectoryInfo(_syncFolder)
                    .GetFiles("*.json")
                    .OrderByDescending(f => f.CreationTime)
                    .Take(10)
                    .Select(f => new
                    {
                        FileName = f.Name,
                        Size = f.Length,
                        SizeMB = Math.Round(f.Length / (1024.0 * 1024), 2),
                        CreationTime = f.CreationTime
                    })
                    .ToList();

                // Đếm các loại sync trong 24h qua
                var last24Hours = DateTime.Now.AddHours(-24);
                var recentFiles = new DirectoryInfo(_syncFolder)
                    .GetFiles("*.json")
                    .Where(f => f.CreationTime >= last24Hours);

                int profileSyncs = recentFiles.Count(f => f.Name.StartsWith("profile_"));
                int batchSyncs = recentFiles.Count(f => f.Name.StartsWith("batch_"));
                int fullSyncs = recentFiles.Count(f => f.Name.StartsWith("fullsync_"));

                // Cập nhật trạng thái hiện tại dựa trên thời gian đồng bộ gần nhất
                lock (_syncStatusLock)
                {
                    if (_currentStatus.LastSync == DateTime.MinValue)
                    {
                        _currentStatus.Status = "Waiting";
                    }
                    else if (DateTime.Now.Subtract(_currentStatus.LastSync).TotalHours > 24)
                    {
                        _currentStatus.Status = "Inactive";
                    }
                    
                    return new
                    {
                        LastSyncFiles = syncFiles,
                        SyncStats = new
                        {
                            Last24Hours = new
                            {
                                ProfileSyncs = profileSyncs,
                                BatchSyncs = batchSyncs,
                                FullSyncs = fullSyncs,
                                TotalSyncs = profileSyncs + batchSyncs + fullSyncs
                            },
                            AllTime = new
                            {
                                TotalSyncs = _currentStatus.TotalSyncCount,
                                TotalProfiles = _currentStatus.TotalProfilesReceived,
                                SuccessCount = _currentStatus.SuccessCount,
                                ErrorCount = _currentStatus.ErrorCount
                            }
                        },
                        CurrentStatus = _currentStatus.Status,
                        LastSync = _currentStatus.LastSync == DateTime.MinValue ? null : _currentStatus.LastSync,
                        LastFullSync = _currentStatus.LastFullSync == DateTime.MinValue ? null : _currentStatus.LastFullSync,
                        LastSuccess = _currentStatus.LastSuccess,
                        LastError = _currentStatus.LastError,
                        LastErrorTime = _currentStatus.LastErrorTime == DateTime.MinValue ? null : _currentStatus.LastErrorTime,
                        TotalProfiles = _profileManager.GetAllProfiles().Count,
                        CurrentTime = DateTime.Now
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thông tin sync");
                return new
                {
                    Error = $"Lỗi khi lấy thông tin sync: {ex.Message}",
                    Status = "Error",
                    CurrentTime = DateTime.Now
                };
            }
        }
    }

    /// <summary>
    /// Thông tin trạng thái đồng bộ
    /// </summary>
    public class SyncStatus
    {
        public DateTime LastSync { get; set; }
        public DateTime LastFullSync { get; set; }
        public int TotalSyncCount { get; set; }
        public int TotalProfilesReceived { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public string LastSuccess { get; set; }
        public string LastError { get; set; }
        public DateTime LastErrorTime { get; set; }
        public string Status { get; set; }
    }
}