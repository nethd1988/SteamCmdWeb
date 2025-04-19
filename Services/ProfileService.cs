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
        private readonly string _profilesPath;
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

            _profilesPath = Path.Combine(dataFolder, "profiles.json");
            if (!File.Exists(_profilesPath))
            {
                File.WriteAllText(_profilesPath, "[]");
                _logger.LogInformation("Đã tạo file profiles.json trống");
            }
        }

        public async Task<List<ClientProfile>> GetAllProfilesAsync()
        {
            try
            {
                string json = await File.ReadAllTextAsync(_profilesPath);
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(json) ?? new List<ClientProfile>();
                _logger.LogInformation("Đã đọc {Count} profiles", profiles.Count);
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc profiles");
                return new List<ClientProfile>();
            }
        }

        public async Task<ClientProfile> GetDecryptedProfileByIdAsync(int id)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Id == id);

                if (profile == null)
                    return null;

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
                    AnonymousLogin = profile.AnonymousLogin,
                    Status = profile.Status,
                    StartTime = profile.StartTime,
                    StopTime = profile.StopTime,
                    Pid = profile.Pid,
                    LastRun = profile.LastRun
                };

                // Giải mã username và password
                if (!string.IsNullOrEmpty(profile.SteamUsername))
                {
                    try
                    {
                        decryptedProfile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Không thể giải mã username cho profile {ProfileId}", id);
                        decryptedProfile.SteamUsername = profile.SteamUsername; // Giữ nguyên giá trị nếu không giải mã được
                    }
                }

                if (!string.IsNullOrEmpty(profile.SteamPassword))
                {
                    try
                    {
                        decryptedProfile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Không thể giải mã password cho profile {ProfileId}", id);
                        decryptedProfile.SteamPassword = profile.SteamPassword; // Giữ nguyên giá trị nếu không giải mã được
                    }
                }

                return decryptedProfile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy và giải mã profile {ProfileId}", id);
                return null;
            }
        }

        public async Task<ClientProfile> GetProfileByIdAsync(int id)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                return profiles.FirstOrDefault(p => p.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy profile {ProfileId}", id);
                return null;
            }
        }

        private async Task SaveProfilesAsync(List<ClientProfile> profiles)
        {
            lock (_fileLock)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(profiles, options);
                File.WriteAllText(_profilesPath, json);
            }

            _logger.LogInformation("Đã lưu {Count} profiles", profiles.Count);
        }

        public async Task<ClientProfile> AddProfileAsync(ClientProfile profile)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();

                // Tạo ID mới
                int nextId = profiles.Count > 0 ? profiles.Max(p => p.Id) + 1 : 1;
                profile.Id = nextId;

                // Đặt các giá trị mặc định
                if (string.IsNullOrEmpty(profile.Status))
                    profile.Status = "Ready";

                if (profile.StartTime == default)
                    profile.StartTime = DateTime.Now;

                if (profile.StopTime == default)
                    profile.StopTime = DateTime.Now;

                if (profile.LastRun == default)
                    profile.LastRun = DateTime.Now;

                profiles.Add(profile);
                await SaveProfilesAsync(profiles);

                _logger.LogInformation("Đã thêm profile mới: {Name} (ID: {Id})", profile.Name, profile.Id);
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
                int index = profiles.FindIndex(p => p.Id == profile.Id);

                if (index == -1)
                {
                    _logger.LogWarning("Không tìm thấy profile có ID {ProfileId} để cập nhật", profile.Id);
                    return false;
                }

                profiles[index] = profile;
                await SaveProfilesAsync(profiles);

                _logger.LogInformation("Đã cập nhật profile: {Name} (ID: {Id})", profile.Name, profile.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật profile {ProfileId}", profile.Id);
                throw;
            }
        }

        public async Task<bool> DeleteProfileAsync(int id)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                int index = profiles.FindIndex(p => p.Id == id);

                if (index == -1)
                {
                    _logger.LogWarning("Không tìm thấy profile có ID {ProfileId} để xóa", id);
                    return false;
                }

                profiles.RemoveAt(index);
                await SaveProfilesAsync(profiles);

                _logger.LogInformation("Đã xóa profile có ID {ProfileId}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile {ProfileId}", id);
                throw;
            }
        }
    }
}