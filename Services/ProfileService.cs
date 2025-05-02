using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class ProfileService
    {
        private readonly ILogger<ProfileService> _logger;
        private readonly DecryptionService _decryptionService;
        private readonly string _profilesFilePath;
        private readonly object _fileLock = new object();

        public ProfileService(
            ILogger<ProfileService> logger,
            DecryptionService decryptionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));

            string dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }

            _profilesFilePath = Path.Combine(dataDirectory, "profiles.json");
            if (!File.Exists(_profilesFilePath))
            {
                File.WriteAllText(_profilesFilePath, "[]");
            }
        }

        public async Task<List<ClientProfile>> GetAllProfilesAsync()
        {
            try
            {
                using (var fileStream = new FileStream(_profilesFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var profiles = await JsonSerializer.DeserializeAsync<List<ClientProfile>>(fileStream, options) ?? new List<ClientProfile>();
                    _logger.LogInformation("Đã đọc {Count} profiles từ file", profiles.Count);
                    return profiles;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc danh sách profiles từ file");
                return new List<ClientProfile>();
            }
        }

        public async Task<ClientProfile> GetProfileByIdAsync(int id)
        {
            var profiles = await GetAllProfilesAsync();
            var profile = profiles.FirstOrDefault(p => p.Id == id);
            if (profile == null)
            {
                _logger.LogWarning("Không tìm thấy profile với ID {Id}", id);
            }
            return profile;
        }

        public async Task<ClientProfile> GetDecryptedProfileByIdAsync(int id)
        {
            var profile = await GetProfileByIdAsync(id);
            if (profile == null)
            {
                return null;
            }

            // Tạo bản sao để không ảnh hưởng đến dữ liệu gốc
            var decryptedProfile = new ClientProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                AppID = profile.AppID,
                InstallDirectory = profile.InstallDirectory,
                Arguments = profile.Arguments,
                ValidateFiles = profile.ValidateFiles,
                AutoRun = profile.AutoRun,
                Status = profile.Status,
                StartTime = profile.StartTime,
                StopTime = profile.StopTime,
                LastRun = profile.LastRun,
                Pid = profile.Pid,
                SteamUsername = profile.SteamUsername,
                SteamPassword = profile.SteamPassword
            };

            try
            {
                // Thử giải mã SteamUsername nếu có
                if (!string.IsNullOrEmpty(decryptedProfile.SteamUsername))
                {
                    try
                    {
                        string decryptedUsername = _decryptionService.DecryptString(decryptedProfile.SteamUsername);
                        // Nếu giải mã thành công và kết quả không rỗng, sử dụng kết quả giải mã
                        if (!string.IsNullOrEmpty(decryptedUsername))
                        {
                            decryptedProfile.SteamUsername = decryptedUsername;
                        }
                        // Nếu không thành công, giữ nguyên giá trị gốc
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Không thể giải mã SteamUsername cho profile {Id}, giữ nguyên giá trị gốc", id);
                        // Giữ nguyên giá trị gốc
                    }
                }

                // Thử giải mã SteamPassword nếu có
                if (!string.IsNullOrEmpty(decryptedProfile.SteamPassword))
                {
                    try
                    {
                        string decryptedPassword = _decryptionService.DecryptString(decryptedProfile.SteamPassword);
                        // Nếu giải mã thành công và kết quả không rỗng, sử dụng kết quả giải mã
                        if (!string.IsNullOrEmpty(decryptedPassword))
                        {
                            decryptedProfile.SteamPassword = decryptedPassword;
                        }
                        // Nếu không thành công, giữ nguyên giá trị gốc
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Không thể giải mã SteamPassword cho profile {Id}, giữ nguyên giá trị gốc", id);
                        // Giữ nguyên giá trị gốc
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tổng thể khi giải mã thông tin đăng nhập cho profile {Id}", id);
            }

            return decryptedProfile;
        }

        public async Task<ClientProfile> AddProfileAsync(ClientProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var profiles = await GetAllProfilesAsync();

            // Mã hóa thông tin đăng nhập
            try
            {
                if (!string.IsNullOrEmpty(profile.SteamUsername))
                {
                    profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                }

                if (!string.IsNullOrEmpty(profile.SteamPassword))
                {
                    profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi mã hóa thông tin đăng nhập");
                throw;
            }

            // Tạo ID mới
            int newId = profiles.Count > 0 ? profiles.Max(p => p.Id) + 1 : 1;
            profile.Id = newId;

            profiles.Add(profile);

            await SaveProfilesAsync(profiles);

            _logger.LogInformation("Đã thêm profile mới với ID {Id}", profile.Id);

            return profile;
        }

        public async Task<bool> UpdateProfileAsync(ClientProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var profiles = await GetAllProfilesAsync();
            var existingProfile = profiles.FirstOrDefault(p => p.Id == profile.Id);

            if (existingProfile == null)
            {
                _logger.LogWarning("Không tìm thấy profile với ID {Id} để cập nhật", profile.Id);
                return false;
            }

            // Mã hóa thông tin đăng nhập
            try
            {
                if (!string.IsNullOrEmpty(profile.SteamUsername))
                {
                    profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                }

                if (!string.IsNullOrEmpty(profile.SteamPassword))
                {
                    profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi mã hóa thông tin đăng nhập");
                throw;
            }

            // Cập nhật profile
            profiles.Remove(existingProfile);
            profiles.Add(profile);

            await SaveProfilesAsync(profiles);

            _logger.LogInformation("Đã cập nhật profile với ID {Id}", profile.Id);

            return true;
        }

        public async Task<bool> DeleteProfileAsync(int id)
        {
            var profiles = await GetAllProfilesAsync();
            var profile = profiles.FirstOrDefault(p => p.Id == id);

            if (profile == null)
            {
                _logger.LogWarning("Không tìm thấy profile với ID {Id} để xóa", id);
                return false;
            }

            profiles.Remove(profile);

            await SaveProfilesAsync(profiles);

            _logger.LogInformation("Đã xóa profile với ID {Id}", id);

            return true;
        }

        private async Task SaveProfilesAsync(List<ClientProfile> profiles)
        {
            lock (_fileLock)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(profiles, options);
                    File.WriteAllText(_profilesFilePath, json);
                    _logger.LogInformation("Đã lưu {Count} profiles vào file", profiles.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi lưu profiles vào file");
                    throw;
                }
            }

            await Task.CompletedTask;
        }

        // Khi lưu profiles ra file JSON, chỉ serialize các trường Name, AppID, SteamUsername, SteamPassword
        public async Task SaveProfilesToFileAsync(IEnumerable<ClientProfile> profiles, string filePath)
        {
            var minimalProfiles = profiles.Select(p => new {
                Name = p.Name,
                AppID = p.AppID,
                SteamUsername = p.SteamUsername,
                SteamPassword = p.SteamPassword
            }).ToList();
            var json = System.Text.Json.JsonSerializer.Serialize(minimalProfiles, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(filePath, json);
        }
    }
}