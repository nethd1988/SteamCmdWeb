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
        private readonly object _fileLock = new object();

        public ProfileService(ILogger<ProfileService> logger, DecryptionService decryptionService)
        {
            _logger = logger;
            _decryptionService = decryptionService;

            string dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
                _logger.LogInformation("Đã tạo thư mục data tại {0}", dataDir);
            }

            _profilesFilePath = Path.Combine(dataDir, "profiles.json");

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
                if (!File.Exists(_profilesFilePath))
                {
                    return new List<ClientProfile>();
                }

                string json = await File.ReadAllTextAsync(_profilesFilePath);
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ClientProfile>();

                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc profiles từ {FilePath}", _profilesFilePath);
                throw;
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

            if (profile != null && !profile.AnonymousLogin)
            {
                // Giải mã thông tin đăng nhập
                if (!string.IsNullOrEmpty(profile.SteamUsername))
                {
                    profile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                }

                if (!string.IsNullOrEmpty(profile.SteamPassword))
                {
                    profile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
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

            // Mã hóa thông tin đăng nhập nếu cần
            if (!profile.AnonymousLogin)
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

            profiles.Add(profile);
            await SaveProfilesAsync(profiles);

            _logger.LogInformation("Đã thêm profile mới: {Name} (ID: {Id})", profile.Name, profile.Id);

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

            // Mã hóa thông tin đăng nhập nếu cần
            if (!profile.AnonymousLogin)
            {
                if (!string.IsNullOrEmpty(profile.SteamUsername) && !profile.SteamUsername.StartsWith("AES:"))
                {
                    profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                }

                if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.SteamPassword.StartsWith("AES:"))
                {
                    profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                }
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

            _logger.LogInformation("Đã xóa profile có ID {Id}", id);

            return true;
        }

        private async Task SaveProfilesAsync(List<ClientProfile> profiles)
        {
            try
            {
                lock (_fileLock)
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(profiles, options);
                    File.WriteAllText(_profilesFilePath, json);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu profiles vào {FilePath}", _profilesFilePath);
                throw;
            }
        }
    }
}