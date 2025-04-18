using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using SteamCmdWeb.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SteamCmdWeb.Services
{
    public class ProfileService
    {
        private readonly ILogger<ProfileService> _logger;
        private readonly IMemoryCache _cache;
        private readonly string _profilesFilePath;
        private const string CacheKey = "AllProfiles";

        public ProfileService(ILogger<ProfileService> logger, IMemoryCache cache)
        {
            _logger = logger;
            _cache = cache;
            var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            _profilesFilePath = Path.Combine(dataFolder, "profiles.json");

            if (!Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
            }

            if (!File.Exists(_profilesFilePath))
            {
                File.WriteAllText(_profilesFilePath, "[]");
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
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(json) ?? new List<ClientProfile>();
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc danh sách profiles");
                return new List<ClientProfile>();
            }
        }

        public async Task<ClientProfile> GetProfileByIdAsync(int id)
        {
            var profiles = await GetAllProfilesAsync();
            return profiles.FirstOrDefault(p => p.Id == id);
        }

        public async Task<ClientProfile> AddProfileAsync(ClientProfile profile)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();

                // Tạo ID mới nếu chưa có
                if (profile.Id <= 0)
                {
                    profile.Id = profiles.Count > 0 ? profiles.Max(p => p.Id) + 1 : 1;
                }

                // Thiết lập giá trị mặc định
                if (string.IsNullOrEmpty(profile.Status))
                {
                    profile.Status = "Ready";
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

        public async Task<bool> UpdateProfileAsync(ClientProfile profile)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                var existingProfile = profiles.FirstOrDefault(p => p.Id == profile.Id);

                if (existingProfile == null)
                {
                    return false;
                }

                var index = profiles.IndexOf(existingProfile);
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

        public async Task<bool> DeleteProfileAsync(int id)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                var profileToRemove = profiles.FirstOrDefault(p => p.Id == id);

                if (profileToRemove == null)
                {
                    return false;
                }

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

                // Cập nhật cache
                _cache.Set(CacheKey, profiles, TimeSpan.FromMinutes(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu danh sách profiles");
                throw;
            }
        }
    }
}