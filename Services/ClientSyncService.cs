using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SteamCmdWeb.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Text.Json.Serialization;

namespace SteamCmdWeb.Services
{
    public class SilentSyncService
    {
        private readonly ILogger<SilentSyncService> _logger;
        private readonly AppProfileManager _profileManager;
        private readonly string _syncFolder;

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
                    return (false, "Dữ liệu profile không hợp lệ");
                }

                // Lưu backup
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"profile_{profile.Id}_{timestamp}.json";
                string filePath = Path.Combine(_syncFolder, fileName);

                string json = System.Text.Json.JsonSerializer.Serialize(profile, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
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
                    return (true, $"Đã thêm profile {profile.Name} (ID: {profile.Id})");
                }
                else
                {
                    // Cập nhật profile hiện có
                    bool updated = _profileManager.UpdateProfile(profile);
                    if (updated)
                    {
                        _logger.LogInformation("Đã cập nhật profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                        return (true, $"Đã cập nhật profile {profile.Name} (ID: {profile.Id})");
                    }
                    else
                    {
                        _logger.LogWarning("Không thể cập nhật profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                        return (false, $"Không thể cập nhật profile {profile.Name} (ID: {profile.Id})");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profile từ client {ClientIp}", clientIp);
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
                    return (false, "Không có profiles để xử lý", 0, 0, 0, new List<int>());
                }

                // Lưu backup của toàn bộ batch
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"batch_{clientIp.Replace(":", "_")}_{timestamp}.json";
                string filePath = Path.Combine(_syncFolder, fileName);

                string json = System.Text.Json.JsonSerializer.Serialize(profiles, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
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

                return (true, resultMessage, addedCount, updatedCount, errorCount, processedIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xử lý batch profiles từ client {ClientIp}", clientIp);
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
                    profiles = System.Text.Json.JsonSerializer.Deserialize<List<ClientProfile>>(jsonData,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            AllowTrailingCommas = true
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi phân tích JSON data từ client {ClientIp}", clientIp);
                    return (false, $"Lỗi phân tích dữ liệu JSON: {ex.Message}", 0, 0, 0, 0);
                }

                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Không có profiles hợp lệ trong full sync từ client {ClientIp}", clientIp);
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
                            addedCount++;
                            _logger.LogInformation("Đã thêm profile mới: {ProfileName} (ID: {ProfileId})", result.Name, result.Id);
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

                return (true, resultMessage, profiles.Count, addedCount, updatedCount, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xử lý full sync từ client {ClientIp}", clientIp);
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
                        }
                    },
                    TotalProfiles = _profileManager.GetAllProfiles().Count,
                    Status = "Active",
                    CurrentTime = DateTime.Now
                };
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
}