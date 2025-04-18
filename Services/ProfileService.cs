using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class ProfileService
    {
        private readonly ILogger<ProfileService> _logger;
        private readonly string _profilesPath;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        public ProfileService(ILogger<ProfileService> logger)
        {
            _logger = logger;
            string dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
                logger.LogInformation("Đã tạo thư mục data tại {0}", dataDir);
            }
            _profilesPath = Path.Combine(dataDir, "profiles.json");

            if (!File.Exists(_profilesPath))
            {
                File.WriteAllText(_profilesPath, "[]");
                logger.LogInformation("Đã tạo file profiles.json trống tại {0}", _profilesPath);
            }
        }

        public async Task<List<ClientProfile>> GetAllProfilesAsync()
        {
            try
            {
                await _fileLock.WaitAsync();
                try
                {
                    if (!File.Exists(_profilesPath))
                    {
                        _logger.LogInformation("File profiles.json không tồn tại. Trả về danh sách rỗng.");
                        return new List<ClientProfile>();
                    }

                    string json = await File.ReadAllTextAsync(_profilesPath);
                    var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ClientProfile>();

                    _logger.LogInformation("Đã đọc {0} profiles từ {1}", profiles.Count, _profilesPath);
                    return profiles;
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc profiles từ {0}", _profilesPath);
                return new List<ClientProfile>();
            }
        }

        public async Task<ClientProfile> GetProfileByIdAsync(int id)
        {
            var profiles = await GetAllProfilesAsync();
            return profiles.FirstOrDefault(p => p.Id == id);
        }

        public async Task<ClientProfile> GetProfileByNameAsync(string name)
        {
            var profiles = await GetAllProfilesAsync();
            return profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<ClientProfile> AddProfileAsync(ClientProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            await _fileLock.WaitAsync();
            try
            {
                var profiles = await GetAllProfilesAsync();

                // Tạo ID mới
                int newId = profiles.Count > 0 ? profiles.Max(p => p.Id) + 1 : 1;
                profile.Id = newId;

                profiles.Add(profile);

                await SaveProfilesAsync(profiles);

                _logger.LogInformation("Đã thêm profile: {0} (ID: {1})", profile.Name, profile.Id);
                return profile;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<bool> UpdateProfileAsync(ClientProfile updatedProfile)
        {
            if (updatedProfile == null)
                throw new ArgumentNullException(nameof(updatedProfile));

            await _fileLock.WaitAsync();
            try
            {
                var profiles = await GetAllProfilesAsync();
                int index = profiles.FindIndex(p => p.Id == updatedProfile.Id);

                if (index < 0)
                {
                    _logger.LogWarning("Không tìm thấy profile có ID {0} để cập nhật", updatedProfile.Id);
                    return false;
                }

                profiles[index] = updatedProfile;
                await SaveProfilesAsync(profiles);

                _logger.LogInformation("Đã cập nhật profile: {0} (ID: {1})", updatedProfile.Name, updatedProfile.Id);
                return true;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<bool> DeleteProfileAsync(int id)
        {
            await _fileLock.WaitAsync();
            try
            {
                var profiles = await GetAllProfilesAsync();
                var profileToRemove = profiles.FirstOrDefault(p => p.Id == id);

                if (profileToRemove == null)
                {
                    _logger.LogWarning("Không tìm thấy profile có ID {0} để xóa", id);
                    return false;
                }

                profiles.Remove(profileToRemove);
                await SaveProfilesAsync(profiles);

                _logger.LogInformation("Đã xóa profile có ID {0}", id);
                return true;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task SaveProfilesAsync(List<ClientProfile> profiles)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                string json = JsonSerializer.Serialize(profiles, options);
                await File.WriteAllTextAsync(_profilesPath, json);

                _logger.LogInformation("Đã lưu {0} profiles vào {1}", profiles.Count, _profilesPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu profiles vào {0}", _profilesPath);
                throw;
            }
        }
    }
}