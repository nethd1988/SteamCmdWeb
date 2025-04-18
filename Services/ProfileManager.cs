using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SteamCmdWeb.Services
{
    public class AppProfileManager
    {
        private readonly ILogger<AppProfileManager> _logger;
        private readonly string _profilesFilePath;
        private readonly string _encryptionKey;

        public AppProfileManager(ILogger<AppProfileManager> logger)
        {
            _logger = logger;
            var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            _profilesFilePath = Path.Combine(dataFolder, "profiles.json");
            _encryptionKey = "SteamCmdWebSecureKey123!@#$%"; // Thường nên lấy từ cấu hình

            if (!Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
            }

            if (!File.Exists(_profilesFilePath))
            {
                File.WriteAllText(_profilesFilePath, "[]");
            }
        }

        public List<ClientProfile> GetAllProfiles()
        {
            try
            {
                if (!File.Exists(_profilesFilePath))
                {
                    return new List<ClientProfile>();
                }

                string json = File.ReadAllText(_profilesFilePath);
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(json) ?? new List<ClientProfile>();
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc danh sách profiles");
                return new List<ClientProfile>();
            }
        }

        public ClientProfile GetProfileById(int id)
        {
            var profiles = GetAllProfiles();
            return profiles.FirstOrDefault(p => p.Id == id);
        }

        public ClientProfile AddProfile(ClientProfile profile)
        {
            try
            {
                var profiles = GetAllProfiles();

                // Tạo ID mới nếu chưa có
                if (profile.Id <= 0)
                {
                    profile.Id = profiles.Count > 0 ? profiles.Max(p => p.Id) + 1 : 1;
                }

                // Xử lý mật khẩu nếu cần
                if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.AnonymousLogin)
                {
                    profile.SteamPassword = EncryptString(profile.SteamPassword);
                }

                profiles.Add(profile);
                SaveProfiles(profiles);

                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm profile mới");
                throw;
            }
        }

        public bool UpdateProfile(ClientProfile profile)
        {
            try
            {
                var profiles = GetAllProfiles();
                var existingProfile = profiles.FirstOrDefault(p => p.Id == profile.Id);

                if (existingProfile == null)
                {
                    return false;
                }

                var index = profiles.IndexOf(existingProfile);
                profiles[index] = profile;

                SaveProfiles(profiles);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật profile");
                return false;
            }
        }

        public bool DeleteProfile(int id)
        {
            try
            {
                var profiles = GetAllProfiles();
                var profileToRemove = profiles.FirstOrDefault(p => p.Id == id);

                if (profileToRemove == null)
                {
                    return false;
                }

                profiles.Remove(profileToRemove);
                SaveProfiles(profiles);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile");
                return false;
            }
        }

        private void SaveProfiles(List<ClientProfile> profiles)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(profiles, options);
                File.WriteAllText(_profilesFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu danh sách profiles");
                throw;
            }
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));
            }
            catch
            {
                return plainText;
            }
        }

        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
            }
            catch
            {
                return cipherText;
            }
        }
    }
}