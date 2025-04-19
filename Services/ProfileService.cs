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
            _logger = logger;
            _decryptionService = decryptionService;

            var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
                _logger.LogInformation("Đã tạo thư mục Data");
            }

            _profilesFilePath = Path.Combine(dataFolder, "profiles.json");
            if (!File.Exists(_profilesFilePath))
            {
                File.WriteAllText(_profilesFilePath, "[]");
                _logger.LogInformation("Đã tạo file profiles.json trống");
            }
        }

        public async Task<List<ClientProfile>> GetAllProfilesAsync()
        {
            try
            {
                string jsonContent;
                lock (_fileLock)
                {
                    jsonContent = File.ReadAllTextAsync(_profilesFilePath).GetAwaiter().GetResult();
                }

                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    return new List<ClientProfile>();
                }

                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(jsonContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return profiles ?? new List<ClientProfile>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc danh sách profiles");
                throw new Exception($"Không thể đọc danh sách profiles: {ex.Message}", ex);
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
            if (profile == null)
            {
                return null;
            }

            if (!profile.AnonymousLogin)
            {
                // Giải mã thông tin đăng nhập
                try
                {
                    if (!string.IsNullOrEmpty(profile.SteamUsername))
                    {
                        profile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                    }

                    if (!string.IsNullOrEmpty(profile.SteamPassword))
                    {
                        profile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi giải mã thông tin đăng nhập cho profile ID {Id}", id);
                }
            }

            return profile;
        }

        public async Task<ClientProfile> AddProfileAsync(ClientProfile profile)
        {
            var profiles = await GetAllProfilesAsync();

            // Tạo ID mới
            int newId = profiles.Count > 0 ? profiles.Max(p => p.Id) + 1 : 1;
            profile.Id = newId;

            // Nếu là đăng nhập ẩn danh, xóa thông tin đăng nhập
            if (profile.AnonymousLogin)
            {
                profile.SteamUsername = string.Empty;
                profile.SteamPassword = string.Empty;
            }

            profiles.Add(profile);
            await SaveProfilesAsync(profiles);

            return profile;
        }

        public async Task<bool> UpdateProfileAsync(ClientProfile profile)
        {
            var profiles = await GetAllProfilesAsync();
            int index = profiles.FindIndex(p => p.Id == profile.Id);
            if (index == -1)
            {
                return false;
            }

            // Nếu là đăng nhập ẩn danh, xóa thông tin đăng nhập
            if (profile.AnonymousLogin)
            {
                profile.SteamUsername = string.Empty;
                profile.SteamPassword = string.Empty;
            }

            profiles[index] = profile;
            await SaveProfilesAsync(profiles);
            return true;
        }

        public async Task<bool> DeleteProfileAsync(int id)
        {
            var profiles = await GetAllProfilesAsync();
            int index = profiles.FindIndex(p => p.Id == id);
            if (index == -1)
            {
                return false;
            }

            profiles.RemoveAt(index);
            await SaveProfilesAsync(profiles);
            return true;
        }

        private async Task SaveProfilesAsync(List<ClientProfile> profiles)
        {
            try
            {
                string json = JsonSerializer.Serialize(profiles,
                    new JsonSerializerOptions { WriteIndented = true });

                lock (_fileLock)
                {
                    File.WriteAllTextAsync(_profilesFilePath, json).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu danh sách profiles");
                throw new Exception($"Không thể lưu danh sách profiles: {ex.Message}", ex);
            }
        }
    }
}