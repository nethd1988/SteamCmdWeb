using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class SilentSyncService
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<SilentSyncService> _logger;
        private readonly string _logPath;
        private readonly object _syncLock = new object();
        
        // Thống kê đồng bộ
        private int _totalSyncCount = 0;
        private int _successSyncCount = 0;
        private int _failedSyncCount = 0;
        private DateTime _lastSyncTime = DateTime.MinValue;
        private int _lastSyncAddedCount = 0;
        private int _lastSyncErrorCount = 0;
        private bool _syncEnabled = true;

        public SilentSyncService(AppProfileManager profileManager, ILogger<SilentSyncService> logger)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");

            if (!Directory.Exists(_logPath))
            {
                Directory.CreateDirectory(_logPath);
            }
        }

        /// <summary>
        /// Nhận một profile từ client
        /// </summary>
        public async Task<(bool Success, string Message)> ReceiveProfileAsync(ClientProfile profile, string clientIp)
        {
            if (!_syncEnabled)
            {
                return (false, "Sync is currently disabled");
            }

            lock (_syncLock)
            {
                _totalSyncCount++;
            }

            try
            {
                if (profile == null)
                {
                    lock (_syncLock)
                    {
                        _failedSyncCount++;
                    }
                    return (false, "Profile is null");
                }

                var existingProfile = _profileManager.GetProfileById(profile.Id);

                if (existingProfile != null)
                {
                    // Profile đã tồn tại, bỏ qua
                    await LogSyncAction($"Profile {profile.Name} (ID: {profile.Id}) already exists, skipped", clientIp);
                    lock (_syncLock)
                    {
                        _successSyncCount++;
                        _lastSyncTime = DateTime.Now;
                    }
                    return (true, $"Profile {profile.Name} already exists, skipped");
                }

                // Thêm mới profile
                await BackupProfileAsync(profile, clientIp);
                _profileManager.AddProfile(profile);
                await LogSyncAction($"Added profile {profile.Name} (ID: {profile.Id})", clientIp);
                
                lock (_syncLock)
                {
                    _successSyncCount++;
                    _lastSyncTime = DateTime.Now;
                    _lastSyncAddedCount++;
                }
                
                return (true, $"Profile {profile.Name} added successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving profile from {ClientIp}", clientIp);
                lock (_syncLock)
                {
                    _failedSyncCount++;
                    _lastSyncErrorCount++;
                }
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Nhận một loạt profiles từ client
        /// </summary>
        public async Task<(bool Success, string Message, int AddedCount, int SkippedCount, int ErrorCount, List<int> ProcessedIds)> 
            ReceiveProfilesAsync(List<ClientProfile> profiles, string clientIp)
        {
            if (!_syncEnabled)
            {
                return (false, "Sync is currently disabled", 0, 0, 0, new List<int>());
            }

            lock (_syncLock)
            {
                _totalSyncCount++;
            }

            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    lock (_syncLock)
                    {
                        _failedSyncCount++;
                    }
                    return (false, "No profiles to process", 0, 0, 0, new List<int>());
                }

                int addedCount = 0;
                int skippedCount = 0;
                int errorCount = 0;
                var processedIds = new List<int>();

                await BackupProfilesAsync(profiles, clientIp);

                foreach (var profile in profiles)
                {
                    try
                    {
                        if (profile == null) continue;

                        var existingProfile = _profileManager.GetProfileById(profile.Id);
                        if (existingProfile == null)
                        {
                            _profileManager.AddProfile(profile);
                            addedCount++;
                            processedIds.Add(profile.Id);
                            await LogSyncAction($"Added profile {profile.Name} (ID: {profile.Id})", clientIp);
                        }
                        else
                        {
                            skippedCount++;
                            await LogSyncAction($"Profile {profile.Name} (ID: {profile.Id}) already exists, skipped", clientIp);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing profile {ProfileId} from {ClientIp}", 
                            profile?.Id, clientIp);
                        errorCount++;
                    }
                }

                lock (_syncLock)
                {
                    _successSyncCount++;
                    _lastSyncTime = DateTime.Now;
                    _lastSyncAddedCount = addedCount;
                    _lastSyncErrorCount = errorCount;
                }

                return (true, $"Processed {profiles.Count} profiles", addedCount, skippedCount, errorCount, processedIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving profile batch from {ClientIp}", clientIp);
                lock (_syncLock)
                {
                    _failedSyncCount++;
                }
                return (false, $"Error: {ex.Message}", 0, 0, profiles.Count, new List<int>());
            }
        }

        /// <summary>
        /// Xử lý full sync từ client
        /// </summary>
        public async Task<(bool Success, string Message, int TotalCount, int AddedCount, int SkippedCount, int ErrorCount)> 
            ProcessFullSyncAsync(string jsonProfiles, string clientIp)
        {
            if (!_syncEnabled)
            {
                return (false, "Sync is currently disabled", 0, 0, 0, 0);
            }

            lock (_syncLock)
            {
                _totalSyncCount++;
            }

            try
            {
                var options = new JsonSerializerOptions { 
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(jsonProfiles, options);
                
                if (profiles == null || profiles.Count == 0)
                {
                    lock (_syncLock)
                    {
                        _failedSyncCount++;
                    }
                    return (false, "No profiles to process", 0, 0, 0, 0);
                }

                await BackupFullSyncDataAsync(jsonProfiles, clientIp);

                int addedCount = 0;
                int skippedCount = 0;
                int errorCount = 0;

                foreach (var profile in profiles)
                {
                    try
                    {
                        if (profile == null) continue;

                        var existingProfile = _profileManager.GetProfileById(profile.Id);
                        if (existingProfile == null)
                        {
                            _profileManager.AddProfile(profile);
                            addedCount++;
                            await LogSyncAction($"Added profile {profile.Name} (ID: {profile.Id})", clientIp);
                        }
                        else
                        {
                            skippedCount++;
                            await LogSyncAction($"Profile {profile.Name} (ID: {profile.Id}) already exists, skipped", clientIp);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing profile {ProfileId} during full sync", profile?.Id);
                        errorCount++;
                    }
                }

                lock (_syncLock)
                {
                    _successSyncCount++;
                    _lastSyncTime = DateTime.Now;
                    _lastSyncAddedCount = addedCount;
                    _lastSyncErrorCount = errorCount;
                }

                return (true, $"Full sync completed: {profiles.Count} profiles processed", 
                    profiles.Count, addedCount, skippedCount, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing full sync from {ClientIp}", clientIp);
                lock (_syncLock)
                {
                    _failedSyncCount++;
                }
                return (false, $"Error: {ex.Message}", 0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Bật/tắt đồng bộ
        /// </summary>
        public void SetSyncEnabled(bool enabled)
        {
            lock (_syncLock)
            {
                _syncEnabled = enabled;
            }
            _logger.LogInformation("Sync is now {Status}", enabled ? "enabled" : "disabled");
        }

        /// <summary>
        /// Lấy thông tin trạng thái đồng bộ
        /// </summary>
        public Dictionary<string, object> GetSyncStatus()
        {
            Dictionary<string, object> status;
            lock (_syncLock)
            {
                status = new Dictionary<string, object>
                {
                    { "LastSyncTime", _lastSyncTime },
                    { "TotalSyncCount", _totalSyncCount },
                    { "SuccessSyncCount", _successSyncCount },
                    { "FailedSyncCount", _failedSyncCount },
                    { "LastSyncAddedCount", _lastSyncAddedCount },
                    { "LastSyncErrorCount", _lastSyncErrorCount },
                    { "SyncEnabled", _syncEnabled },
                    { "CurrentTime", DateTime.Now }
                };
            }
            return status;
        }

        /// <summary>
        /// Ghi log hành động đồng bộ
        /// </summary>
        private async Task LogSyncAction(string message, string clientIp)
        {
            string logFile = Path.Combine(_logPath, $"silentsync_{DateTime.Now:yyyyMMdd}.log");
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {clientIp} - {message}{Environment.NewLine}";
            await File.AppendAllTextAsync(logFile, logEntry);
        }

        /// <summary>
        /// Sao lưu profile nhận được
        /// </summary>
        private async Task BackupProfileAsync(ClientProfile profile, string clientIp)
        {
            try
            {
                string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backup", "SilentSync");
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"profile_{profile.Id}_{clientIp.Replace(':', '_')}_{timestamp}.json";
                string filePath = Path.Combine(backupFolder, fileName);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonContent = JsonSerializer.Serialize(profile, options);
                await File.WriteAllTextAsync(filePath, jsonContent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error backing up profile {ProfileId} from {ClientIp}", 
                    profile.Id, clientIp);
            }
        }

        /// <summary>
        /// Sao lưu danh sách profiles nhận được
        /// </summary>
        private async Task BackupProfilesAsync(List<ClientProfile> profiles, string clientIp)
        {
            try
            {
                if (profiles == null || profiles.Count == 0) return;

                string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backup", "SilentSync");
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"batch_{clientIp.Replace(':', '_')}_{timestamp}.json";
                string filePath = Path.Combine(backupFolder, fileName);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonContent = JsonSerializer.Serialize(profiles, options);
                await File.WriteAllTextAsync(filePath, jsonContent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error backing up profiles batch from {ClientIp}", clientIp);
            }
        }

        /// <summary>
        /// Sao lưu dữ liệu full sync
        /// </summary>
        private async Task BackupFullSyncDataAsync(string jsonData, string clientIp)
        {
            try
            {
                string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backup", "FullSync");
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"full_{clientIp.Replace(':', '_')}_{timestamp}.json";
                string filePath = Path.Combine(backupFolder, fileName);

                await File.WriteAllTextAsync(filePath, jsonData);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error backing up full sync data from {ClientIp}", clientIp);
            }
        }

        /// <summary>
        /// Reset thống kê đồng bộ
        /// </summary>
        public void ResetSyncStats()
        {
            lock (_syncLock)
            {
                _totalSyncCount = 0;
                _successSyncCount = 0;
                _failedSyncCount = 0;
                _lastSyncAddedCount = 0;
                _lastSyncErrorCount = 0;
            }
            _logger.LogInformation("Sync statistics have been reset");
        }
    }
}