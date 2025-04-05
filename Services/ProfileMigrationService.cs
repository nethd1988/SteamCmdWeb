using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    /// <summary>
    /// Dịch vụ quản lý di chuyển và sao lưu profile
    /// </summary>
    public class ProfileMigrationService
    {
        private readonly ILogger<ProfileMigrationService> _logger;
        private readonly AppProfileManager _appProfileManager;
        private readonly string _backupFolder;

        /// <summary>
        /// Khởi tạo dịch vụ di chuyển profile
        /// </summary>
        public ProfileMigrationService(
            ILogger<ProfileMigrationService> logger,
            AppProfileManager appProfileManager)
        {
            _logger = logger;
            _appProfileManager = appProfileManager;

            // Tạo thư mục backup nếu chưa tồn tại
            _backupFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Data",
                "Backup"
            );

            try
            {
                Directory.CreateDirectory(_backupFolder);
                _logger.LogInformation("Backup directory created at: {Path}", _backupFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể tạo thư mục backup");
            }
        }

        /// <summary>
        /// Di chuyển profiles sang AppProfiles với tùy chọn bỏ qua kiểm tra trùng
        /// </summary>
        public async Task<(int Added, int Skipped)> MigrateProfilesToAppProfiles(
            List<ClientProfile> profiles,
            bool skipDuplicateCheck = false)
        {
            int added = 0;
            int skipped = 0;

            if (profiles == null || profiles.Count == 0)
            {
                _logger.LogWarning("Không có profiles để di chuyển");
                return (added, skipped);
            }

            // Lấy danh sách profiles hiện tại
            var existingProfiles = _appProfileManager.GetAllProfiles();

            // Theo dõi các profiles đã xử lý
            var processedProfileIds = new HashSet<int>();
            var processedUsernames = new HashSet<string>();

            foreach (var profile in profiles)
            {
                try
                {
                    if (profile == null) continue;

                    // Kiểm tra profile đã được xử lý chưa
                    if (profile.Id > 0 && processedProfileIds.Contains(profile.Id))
                    {
                        skipped++;
                        continue;
                    }

                    // Kiểm tra trùng lặp
                    bool isDuplicate = existingProfiles.Any(p =>
                        p.Id == profile.Id ||
                        (p.Name == profile.Name && p.AppID == profile.AppID)
                    );

                    // Kiểm tra trùng username
                    bool hasDuplicateCredentials = false;
                    if (!skipDuplicateCheck && !string.IsNullOrEmpty(profile.SteamUsername))
                    {
                        hasDuplicateCredentials = existingProfiles.Any(p =>
                            p.SteamUsername == profile.SteamUsername) ||
                            processedUsernames.Contains(profile.SteamUsername);
                    }

                    // Bỏ qua nếu trùng lặp
                    if (!skipDuplicateCheck && hasDuplicateCredentials)
                    {
                        _logger.LogInformation(
                            "Bỏ qua profile {Name} (ID: {Id}) - username trùng lặp",
                            profile.Name,
                            profile.Id
                        );
                        skipped++;
                        continue;
                    }

                    // Đảm bảo profile có ID mới nếu đã tồn tại
                    if (isDuplicate)
                    {
                        profile.Id = 0; // Để AppProfileManager tự sinh ID mới
                    }

                    // Thêm profile
                    var addedProfile = _appProfileManager.AddProfile(profile);
                    added++;

                    // Theo dõi các profile đã xử lý
                    processedProfileIds.Add(addedProfile.Id);
                    if (!string.IsNullOrEmpty(profile.SteamUsername))
                    {
                        processedUsernames.Add(profile.SteamUsername);
                    }

                    _logger.LogInformation(
                        "Đã thêm profile {Name} (ID: {Id}) vào AppProfiles",
                        addedProfile.Name,
                        addedProfile.Id
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Lỗi khi thêm profile {Name} (ID: {Id}) vào AppProfiles",
                        profile?.Name,
                        profile?.Id
                    );
                    skipped++;
                }
            }

            return (added, skipped);
        }

        /// <summary>
        /// Sao lưu danh sách profiles
        /// </summary>
        public async Task<string> BackupClientProfiles(List<ClientProfile> profiles)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Không có profiles để sao lưu");
                    return "Không có profiles để sao lưu";
                }

                // Tạo tên file backup
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"client_backup_{timestamp}.json";
                string filePath = Path.Combine(_backupFolder, fileName);

                // Cấu hình serialize JSON
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                // Chuyển đổi vàRandn chèn và ghi file
                string jsonContent = JsonSerializer.Serialize(profiles, options);
                await File.WriteAllTextAsync(filePath, jsonContent);

                _logger.LogInformation(
                    "Đã sao lưu {Count} profiles tại {FilePath}",
                    profiles.Count,
                    filePath
                );

                return $"Đã sao lưu {profiles.Count} profiles tại {fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi sao lưu profiles");
                return $"Lỗi: {ex.Message}";
            }
        }

        /// <summary>
        /// Lấy danh sách các file backup
        /// </summary>
        public List<BackupInfo> GetBackupFiles()
        {
            try
            {
                return new DirectoryInfo(_backupFolder)
                    .GetFiles("*.json")
                    .OrderByDescending(f => f.CreationTime)
                    .Take(50) // Giới hạn 50 file backup mới nhất
                    .Select(f => new BackupInfo
                    {
                        FileName = f.Name,
                        CreationTime = f.CreationTime,
                        SizeMB = Math.Round(f.Length / (1024.0 * 1024), 2),
                        FullPath = f.FullName
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách backup");
                return new List<BackupInfo>();
            }
        }

        /// <summary>
        /// Tải profiles từ file backup
        /// </summary>
        public async Task<List<ClientProfile>> LoadProfilesFromBackup(string fileName)
        {
            try
            {
                string filePath = Path.Combine(_backupFolder, fileName);
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Không tìm thấy file backup: {FilePath}", filePath);
                    return new List<ClientProfile>();
                }

                string jsonContent = await File.ReadAllTextAsync(filePath);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    _logger.LogWarning("File backup trống: {FileName}", fileName);
                    return new List<ClientProfile>();
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                List<ClientProfile> profiles;
                try
                {
                    // Thử deserialize thành List<ClientProfile>
                    profiles = JsonSerializer.Deserialize<List<ClientProfile>>(jsonContent, options);
                }
                catch (JsonException)
                {
                    // Nếu thất bại, thử deserialize thành ClientProfile đơn lẻ
                    var singleProfile = JsonSerializer.Deserialize<ClientProfile>(jsonContent, options);
                    profiles = singleProfile != null ? new List<ClientProfile> { singleProfile } : new List<ClientProfile>();
                }

                // Lọc bỏ các profile không hợp lệ
                profiles = profiles
                    .Where(p => p != null && !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(p.AppID))
                    .ToList();

                _logger.LogInformation("Đã tải {Count} profiles từ file backup {FileName}", profiles.Count, fileName);
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải profiles từ file backup {FileName}", fileName);
                return new List<ClientProfile>();
            }
        }
    }

    /// <summary>
    /// Thông tin về file backup
    /// </summary>
    public class BackupInfo
    {
        /// <summary>
        /// Tên file backup
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Thời gian tạo backup
        /// </summary>
        public DateTime CreationTime { get; set; }

        /// <summary>
        /// Kích thước file backup (MB)
        /// </summary>
        public double SizeMB { get; set; }

        /// <summary>
        /// Đường dẫn đầy đủ của file backup
        /// </summary>
        public string FullPath { get; set; }
    }
}