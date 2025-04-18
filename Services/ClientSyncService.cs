using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SteamCmdWeb.Services
{
    public class SilentSyncService
    {
        private readonly ILogger<SilentSyncService> _logger;
        private readonly AppProfileManager _profileManager;
        private readonly string _syncStatsFilePath;
        private SyncStatusResponse _lastSyncStatus;

        public SilentSyncService(ILogger<SilentSyncService> logger, AppProfileManager profileManager)
        {
            _logger = logger;
            _profileManager = profileManager;

            var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            var syncFolder = Path.Combine(dataFolder, "Sync");
            _syncStatsFilePath = Path.Combine(syncFolder, "sync_stats.json");

            if (!Directory.Exists(syncFolder))
            {
                Directory.CreateDirectory(syncFolder);
            }

            _lastSyncStatus = LoadSyncStatus();
        }

        public async Task<(bool success, string message)> ReceiveProfileAsync(ClientProfile profile, string clientIp)
        {
            try
            {
                if (profile == null)
                {
                    return (false, "Profile không hợp lệ");
                }

                var existingProfiles = _profileManager.GetAllProfiles();
                var existingProfile = existingProfiles.FirstOrDefault(p =>
                    p.Name == profile.Name &&
                    p.AppID == profile.AppID &&
                    p.InstallDirectory == profile.InstallDirectory);

                if (existingProfile != null)
                {
                    // Cập nhật thông tin
                    profile.Id = existingProfile.Id;
                    _profileManager.UpdateProfile(profile);

                    await LogSyncActivity(clientIp, "update", 1);
                    return (true, $"Đã cập nhật profile '{profile.Name}'");
                }
                else
                {
                    // Tạo mới
                    _profileManager.AddProfile(profile);

                    await LogSyncActivity(clientIp, "add", 1);
                    return (true, $"Đã thêm profile mới '{profile.Name}'");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profile từ IP {ClientIp}", clientIp);
                return (false, $"Lỗi: {ex.Message}");
            }
        }

        public async Task<(bool success, string message, int added, int skipped, int errors, List<int> processedIds)> ReceiveProfilesAsync(List<ClientProfile> profiles, string clientIp)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    return (false, "Không có profiles để xử lý", 0, 0, 0, new List<int>());
                }

                int added = 0;
                int skipped = 0;
                int errors = 0;
                var processedIds = new List<int>();
                var existingProfiles = _profileManager.GetAllProfiles();

                foreach (var profile in profiles)
                {
                    try
                    {
                        if (profile == null) continue;

                        var existingProfile = existingProfiles.FirstOrDefault(p =>
                            p.Name == profile.Name &&
                            p.AppID == profile.AppID &&
                            p.InstallDirectory == profile.InstallDirectory);

                        if (existingProfile != null)
                        {
                            // Cập nhật thông tin hiện có
                            profile.Id = existingProfile.Id;
                            _profileManager.UpdateProfile(profile);
                            skipped++;
                        }
                        else
                        {
                            // Thêm mới profile
                            var newProfile = _profileManager.AddProfile(profile);
                            processedIds.Add(newProfile.Id);
                            added++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile '{ProfileName}' từ IP {ClientIp}", profile?.Name, clientIp);
                        errors++;
                    }
                }

                await LogSyncActivity(clientIp, "batch", added, skipped, errors);

                string message = $"Đã xử lý {profiles.Count} profile. Thêm mới: {added}, Bỏ qua: {skipped}, Lỗi: {errors}";
                return (true, message, added, skipped, errors, processedIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận batch profiles từ IP {ClientIp}", clientIp);
                return (false, $"Lỗi: {ex.Message}", 0, 0, 0, new List<int>());
            }
        }

        public async Task<(bool success, string message, int totalProfiles, int added, int skipped, int errors)> ProcessFullSyncAsync(string requestBody, string clientIp)
        {
            try
            {
                if (string.IsNullOrEmpty(requestBody))
                {
                    return (false, "Request body rỗng", 0, 0, 0, 0);
                }

                List<ClientProfile> profiles;
                try
                {
                    profiles = JsonSerializer.Deserialize<List<ClientProfile>>(requestBody);
                }
                catch
                {
                    // Thử phân tích dưới dạng profile đơn lẻ
                    try
                    {
                        var singleProfile = JsonSerializer.Deserialize<ClientProfile>(requestBody);
                        profiles = new List<ClientProfile> { singleProfile };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Không thể phân tích dữ liệu JSON từ IP {ClientIp}", clientIp);
                        return (false, "Dữ liệu JSON không hợp lệ", 0, 0, 0, 0);
                    }
                }

                if (profiles == null || profiles.Count == 0)
                {
                    return (false, "Không có profiles để xử lý", 0, 0, 0, 0);
                }

                int added = 0;
                int skipped = 0;
                int errors = 0;
                var existingProfiles = _profileManager.GetAllProfiles();

                foreach (var profile in profiles)
                {
                    try
                    {
                        if (profile == null) continue;

                        var existingProfile = existingProfiles.FirstOrDefault(p =>
                            p.Name == profile.Name &&
                            p.AppID == profile.AppID &&
                            p.InstallDirectory == profile.InstallDirectory);

                        if (existingProfile != null)
                        {
                            // Cập nhật thông tin hiện có
                            profile.Id = existingProfile.Id;
                            _profileManager.UpdateProfile(profile);
                            skipped++;
                        }
                        else
                        {
                            // Thêm mới profile
                            _profileManager.AddProfile(profile);
                            added++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile '{ProfileName}' từ IP {ClientIp}", profile?.Name, clientIp);
                        errors++;
                    }
                }

                await LogSyncActivity(clientIp, "full", added, skipped, errors);

                string message = $"Đã xử lý {profiles.Count} profile. Thêm mới: {added}, Cập nhật: {skipped}, Lỗi: {errors}";
                return (true, message, profiles.Count, added, skipped, errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý full sync từ IP {ClientIp}", clientIp);
                return (false, $"Lỗi: {ex.Message}", 0, 0, 0, 0);
            }
        }

        public SyncStatusResponse GetSyncStatus()
        {
            _lastSyncStatus.CurrentTime = DateTime.Now;
            return _lastSyncStatus;
        }

        private async Task LogSyncActivity(string clientIp, string syncType, int added = 0, int updated = 0, int errors = 0)
        {
            try
            {
                string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string logFilePath = Path.Combine(logDir, $"silentsync_{DateTime.Now:yyyyMMdd}.log");
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {clientIp} - {syncType} - Added: {added}, Updated: {updated}, Errors: {errors}{Environment.NewLine}";

                await File.AppendAllTextAsync(logFilePath, logEntry);

                // Cập nhật thống kê đồng bộ
                _lastSyncStatus.LastSyncTime = DateTime.Now;
                _lastSyncStatus.TotalSyncCount++;
                _lastSyncStatus.SuccessSyncCount += (errors == 0) ? 1 : 0;
                _lastSyncStatus.FailedSyncCount += (errors > 0) ? 1 : 0;
                _lastSyncStatus.LastSyncAddedCount = added;
                _lastSyncStatus.LastSyncUpdatedCount = updated;
                _lastSyncStatus.LastSyncErrorCount = errors;
                _lastSyncStatus.SyncEnabled = true;

                await SaveSyncStats();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi ghi log sync");
            }
        }

        private SyncStatusResponse LoadSyncStatus()
        {
            try
            {
                if (File.Exists(_syncStatsFilePath))
                {
                    string json = File.ReadAllText(_syncStatsFilePath);
                    return JsonSerializer.Deserialize<SyncStatusResponse>(json)
                        ?? CreateDefaultSyncStatus();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc thống kê sync");
            }

            return CreateDefaultSyncStatus();
        }

        private async Task SaveSyncStats()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_lastSyncStatus, options);
                await File.WriteAllTextAsync(_syncStatsFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu thống kê sync");
            }
        }

        private SyncStatusResponse CreateDefaultSyncStatus()
        {
            return new SyncStatusResponse
            {
                LastSyncTime = DateTime.MinValue,
                TotalSyncCount = 0,
                SuccessSyncCount = 0,
                FailedSyncCount = 0,
                LastSyncAddedCount = 0,
                LastSyncUpdatedCount = 0,
                LastSyncErrorCount = 0,
                SyncEnabled = true,
                CurrentTime = DateTime.Now
            };
        }
    }
}