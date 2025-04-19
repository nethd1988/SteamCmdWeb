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
        private readonly string _profilesFilePath;
        private readonly DecryptionService _decryptionService;

        public ProfileService(ILogger<ProfileService> logger, DecryptionService decryptionService)
        {
            _logger = logger;
            _decryptionService = decryptionService;

            var currentDir = Directory.GetCurrentDirectory();
            string dataDir = Path.Combine(currentDir, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
                _logger.LogInformation("Đã tạo thư mục Data tại {Path}", dataDir);
            }

            _profilesFilePath = Path.Combine(dataDir, "profiles.json");
            if (!File.Exists(_profilesFilePath))
            {
                File.WriteAllText(_profilesFilePath, "[]");
                _logger.LogInformation("Đã tạo file profiles.json trống tại {Path}", _profilesFilePath);
            }
        }

        public async Task<List<ClientProfile>> GetAllProfilesAsync()
        {
            try
            {
                if (!File.Exists(_profilesFilePath))
                {
                    return new List<ClientProfile>();
                }

                string json = await File.ReadAllTextAsync(_profilesFilePath);
                return JsonSerializer.Deserialize<List<ClientProfile>>(json) ?? new List<ClientProfile>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc file profiles.json");
                return new List<ClientProfile>();
            }
        }

        public async Task<ClientProfile> GetProfileByIdAsync(int id)
        {
            var profiles = await GetAllProfilesAsync();
            return profiles.FirstOrDefault(p => p.Id == id);
        }

        public async Task<ClientProfile> GetDecryptedProfileByIdAsync(int id)
        {
            var profile = await GetProfileByIdAsync(id);
            if (profile == null) return null;

            // Tạo bản sao để không sửa đổi profile gốc
            var decryptedProfile = new ClientProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                AppID = profile.AppID,
                InstallDirectory = profile.InstallDirectory,
                Arguments = profile.Arguments,
                ValidateFiles = profile.ValidateFiles,
                AutoRun = profile.AutoRun,
                AnonymousLogin = profile.AnonymousLogin,
                Status = profile.Status,
                StartTime = profile.StartTime,
                StopTime = profile.StopTime,
                Pid = profile.Pid,
                LastRun = profile.LastRun,
                InstallSize = profile.InstallSize,
                SessionCount = profile.SessionCount,
                Notes = profile.Notes
            };

            // Giải mã thông tin đăng nhập nếu không phải ẩn danh
            if (!profile.AnonymousLogin)
            {
                if (!string.IsNullOrEmpty(profile.SteamUsername))
                {
                    decryptedProfile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                }

                if (!string.IsNullOrEmpty(profile.SteamPassword))
                {
                    decryptedProfile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                }
            }
            else
            {
                decryptedProfile.SteamUsername = string.Empty;
                decryptedProfile.SteamPassword = string.Empty;
            }

            return decryptedProfile;
        }

        public async Task<bool> UpdateProfileAsync(ClientProfile profile)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                int index = profiles.FindIndex(p => p.Id == profile.Id);
                if (index == -1) return false;

                // Mã hóa thông tin đăng nhập nếu không phải ẩn danh và được cập nhật
                if (!profile.AnonymousLogin)
                {
                    var existingProfile = profiles[index];

                    // Kiểm tra và mã hóa username nếu đã thay đổi
                    if (!string.IsNullOrEmpty(profile.SteamUsername) &&
                        (existingProfile.SteamUsername != profile.SteamUsername) &&
                        !profile.SteamUsername.StartsWith("AQA") &&
                        !profile.SteamUsername.StartsWith("eyJ"))
                    {
                        profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                    }

                    // Kiểm tra và mã hóa password nếu đã thay đổi
                    if (!string.IsNullOrEmpty(profile.SteamPassword) &&
                        (existingProfile.SteamPassword != profile.SteamPassword) &&
                        !profile.SteamPassword.StartsWith("AQA") &&
                        !profile.SteamPassword.StartsWith("eyJ"))
                    {
                        profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                    }
                }
                else
                {
                    profile.SteamUsername = string.Empty;
                    profile.SteamPassword = string.Empty;
                }

                profiles[index] = profile;
                await SaveProfilesAsync(profiles);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật profile");
                return false;
            }
        }

        public async Task<ClientProfile> AddProfileAsync(ClientProfile profile)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();

                // Gán ID mới
                int newId = 1;
                if (profiles.Count > 0)
                {
                    newId = profiles.Max(p => p.Id) + 1;
                }
                profile.Id = newId;

                // Mã hóa thông tin đăng nhập nếu không phải ẩn danh
                if (!profile.AnonymousLogin)
                {
                    if (!string.IsNullOrEmpty(profile.SteamUsername) &&
                        !profile.SteamUsername.StartsWith("AQA") &&
                        !profile.SteamUsername.StartsWith("eyJ"))
                    {
                        profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                    }

                    if (!string.IsNullOrEmpty(profile.SteamPassword) &&
                        !profile.SteamPassword.StartsWith("AQA") &&
                        !profile.SteamPassword.StartsWith("eyJ"))
                    {
                        profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                    }
                }
                else
                {
                    profile.SteamUsername = string.Empty;
                    profile.SteamPassword = string.Empty;
                }

                profiles.Add(profile);
                await SaveProfilesAsync(profiles);
                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm profile mới");
                throw;
            }
        }

        public async Task<bool> DeleteProfileAsync(int id)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                var profileToRemove = profiles.FirstOrDefault(p => p.Id == id);
                if (profileToRemove == null) return false;

                profiles.Remove(profileToRemove);
                await SaveProfilesAsync(profiles);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile");
                return false;
            }
        }

        private async Task SaveProfilesAsync(List<ClientProfile> profiles)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(profiles, options);
                await File.WriteAllTextAsync(_profilesFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu file profiles.json");
                throw;
            }
        }
    }
}