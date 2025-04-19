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

        public ProfileService(ILogger<ProfileService> logger, DecryptionService decryptionService)
        {
            _logger = logger;
            _decryptionService = decryptionService;

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
                string json = await File.ReadAllTextAsync(_profilesFilePath);
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(json) ?? new List<ClientProfile>();
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc file profiles");
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

            if (profile != null && !profile.AnonymousLogin)
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

            return profile;
        }

        public async Task<ClientProfile> AddProfileAsync(ClientProfile profile)
        {
            lock (_fileLock)
            {
                var profiles = GetAllProfilesAsync().Result;

                // Đặt ID mới
                int newId = profiles.Count > 0 ? profiles.Max(p => p.Id) + 1 : 1;
                profile.Id = newId;

                // Đặt các giá trị mặc định
                if (string.IsNullOrEmpty(profile.Status))
                    profile.Status = "Ready";

                profile.StartTime = DateTime.Now;
                profile.StopTime = DateTime.Now;
                profile.LastRun = DateTime.UtcNow;
                profile.Pid = 0;

                profiles.Add(profile);
                SaveProfilesAsync(profiles).Wait();

                return profile;
            }
        }

        public async Task<bool> UpdateProfileAsync(ClientProfile profile)
        {
            lock (_fileLock)
            {
                var profiles = GetAllProfilesAsync().Result;
                int index = profiles.FindIndex(p => p.Id == profile.Id);

                if (index == -1)
                    return false;

                profiles[index] = profile;
                SaveProfilesAsync(profiles).Wait();

                return true;
            }
        }

        public async Task<bool> DeleteProfileAsync(int id)
        {
            lock (_fileLock)
            {
                var profiles = GetAllProfilesAsync().Result;
                int index = profiles.FindIndex(p => p.Id == id);

                if (index == -1)
                    return false;

                profiles.RemoveAt(index);
                SaveProfilesAsync(profiles).Wait();

                return true;
            }
        }

        private async Task SaveProfilesAsync(List<ClientProfile> profiles)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(profiles, options);
            await File.WriteAllTextAsync(_profilesFilePath, json);
        }
    }
}