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
    public class ProfileMigrationService
    {
        private readonly ILogger<ProfileMigrationService> _logger;
        private readonly AppProfileManager _profileManager;
        private readonly string _backupFolder;

        public ProfileMigrationService(ILogger<ProfileMigrationService> logger, AppProfileManager profileManager)
        {
            _logger = logger;
            _profileManager = profileManager;

            var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            _backupFolder = Path.Combine(dataFolder, "Backup");

            if (!Directory.Exists(_backupFolder))
            {
                Directory.CreateDirectory(_backupFolder);
            }
        }

        public List<BackupFileInfo> GetBackupFiles()
        {
            try
            {
                var result = new List<BackupFileInfo>();
                var directory = new DirectoryInfo(_backupFolder);

                if (!directory.Exists)
                {
                    return result;
                }

                var files = directory.GetFiles("*.json").OrderByDescending(f => f.LastWriteTime);

                foreach (var file in files)
                {
                    result.Add(new BackupFileInfo
                    {
                        FileName = file.Name,
                        CreationTime = file.CreationTime,
                        LastWriteTime = file.LastWriteTime,
                        SizeBytes = file.Length,
                        SizeMB = Math.Round(file.Length / 1024.0 / 1024.0, 2)
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách file backup");
                return new List<BackupFileInfo>();
            }
        }

        public async Task<string> BackupClientProfiles(List<ClientProfile> profiles)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    return "Không có profile nào để backup";
                }

                if (!Directory.Exists(_backupFolder))
                {
                    Directory.CreateDirectory(_backupFolder);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"backup_{timestamp}.json";
                string filePath = Path.Combine(_backupFolder, fileName);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(profiles, options);
                await File.WriteAllTextAsync(filePath, json);

                return $"Đã backup {profiles.Count} profile vào file {fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi backup profiles");
                throw;
            }
        }

        public async Task<List<ClientProfile>> LoadProfilesFromBackup(string fileName)
        {
            try
            {
                string filePath = Path.Combine(_backupFolder, fileName);

                if (!File.Exists(filePath))
                {
                    return new List<ClientProfile>();
                }

                string json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<List<ClientProfile>>(json) ?? new List<ClientProfile>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc profiles từ backup");
                throw;
            }
        }

        public async Task<Tuple<int, int>> MigrateProfilesToAppProfiles(List<ClientProfile> profiles, bool skipDuplicateCheck = false)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    return new Tuple<int, int>(0, 0);
                }

                int added = 0;
                int skipped = 0;
                var existingProfiles = _profileManager.GetAllProfiles();

                foreach (var profile in profiles)
                {
                    // Kiểm tra trùng lặp nếu không bỏ qua
                    if (!skipDuplicateCheck)
                    {
                        bool isDuplicate = existingProfiles.Any(p =>
                            p.Name == profile.Name &&
                            p.AppID == profile.AppID &&
                            p.InstallDirectory == profile.InstallDirectory);

                        if (isDuplicate)
                        {
                            skipped++;
                            continue;
                        }
                    }

                    // Đặt ID mới để tránh xung đột
                    profile.Id = 0;

                    // Đặt trạng thái mặc định
                    if (string.IsNullOrEmpty(profile.Status))
                    {
                        profile.Status = "Ready";
                    }

                    _profileManager.AddProfile(profile);
                    added++;
                }

                return new Tuple<int, int>(added, skipped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi di chuyển profiles");
                throw;
            }
        }
    }

    public class BackupFileInfo
    {
        public string FileName { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastWriteTime { get; set; }
        public long SizeBytes { get; set; }
        public double SizeMB { get; set; }
    }
}