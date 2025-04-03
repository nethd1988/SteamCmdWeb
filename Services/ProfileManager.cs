using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class ProfileManager
    {
        private readonly ILogger<ProfileManager> _logger;
        private readonly string _dataFolder;
        private readonly string _profilesFilePath;
        private readonly string _encryptionKey = "yourEncryptionKey123!@#";
        private static int _nextId = 1;

        public ProfileManager(ILogger<ProfileManager> logger)
        {
            _logger = logger;
            _dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(_dataFolder))
            {
                Directory.CreateDirectory(_dataFolder);
            }
            _profilesFilePath = Path.Combine(_dataFolder, "profiles.json");

            // Initialize the next ID based on existing profiles
            var profiles = GetAllProfiles();
            if (profiles.Any())
            {
                _nextId = profiles.Max(p => p.Id) + 1;
            }
        }

        public List<ClientProfile> GetAllProfiles()
        {
            if (!File.Exists(_profilesFilePath))
            {
                return new List<ClientProfile>();
            }

            try
            {
                string jsonContent = File.ReadAllText(_profilesFilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<ClientProfile>>(jsonContent, options) ?? new List<ClientProfile>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading profiles file");
                return new List<ClientProfile>();
            }
        }

        public ClientProfile GetProfileById(int id)
        {
            return GetAllProfiles().FirstOrDefault(p => p.Id == id);
        }

        public ClientProfile GetProfileByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            return GetAllProfiles().FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public void SaveProfiles(List<ClientProfile> profiles)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonContent = JsonSerializer.Serialize(profiles, options);
            File.WriteAllText(_profilesFilePath, jsonContent);
        }

        public ClientProfile AddProfile(ClientProfile profile)
        {
            var profiles = GetAllProfiles();

            // Kiểm tra xem profile có tồn tại trước đó dựa trên tên và AppID
            var existingProfile = FindDuplicateProfile(profile);
            if (existingProfile != null)
            {
                // Cập nhật profile thay vì thêm mới
                existingProfile.Name = profile.Name;
                existingProfile.AppID = profile.AppID;
                existingProfile.InstallDirectory = profile.InstallDirectory;
                existingProfile.SteamUsername = profile.SteamUsername;
                existingProfile.SteamPassword = profile.SteamPassword;
                existingProfile.Arguments = profile.Arguments;
                existingProfile.ValidateFiles = profile.ValidateFiles;
                existingProfile.AutoRun = profile.AutoRun;
                existingProfile.AnonymousLogin = profile.AnonymousLogin;
                // Giữ nguyên các trường trạng thái

                SaveProfiles(profiles);
                return existingProfile;
            }

            // Set new ID if not provided
            if (profile.Id <= 0)
            {
                profile.Id = _nextId++;
            }

            // Set current time
            profile.StartTime = DateTime.Now;
            profile.StopTime = DateTime.Now;
            profile.LastRun = DateTime.UtcNow;
            profile.Status = "Stopped";
            profile.Pid = 0;

            profiles.Add(profile);
            SaveProfiles(profiles);

            _logger.LogInformation("Added new profile: {Name} with ID {Id}", profile.Name, profile.Id);
            return profile;
        }

        public bool UpdateProfile(ClientProfile updatedProfile)
        {
            var profiles = GetAllProfiles();
            int index = profiles.FindIndex(p => p.Id == updatedProfile.Id);

            if (index >= 0)
            {
                profiles[index] = updatedProfile;
                SaveProfiles(profiles);
                _logger.LogInformation("Updated profile: {Name} with ID {Id}", updatedProfile.Name, updatedProfile.Id);
                return true;
            }

            _logger.LogWarning("Failed to update profile with ID {Id}: Profile not found", updatedProfile.Id);
            return false;
        }

        public bool DeleteProfile(int id)
        {
            var profiles = GetAllProfiles();
            int initialCount = profiles.Count;
            var profileToRemove = profiles.FirstOrDefault(p => p.Id == id);

            if (profileToRemove != null)
            {
                _logger.LogInformation("Deleting profile: {Name} with ID {Id}", profileToRemove.Name, profileToRemove.Id);
                profiles.Remove(profileToRemove);
                SaveProfiles(profiles);
                return true;
            }

            _logger.LogWarning("Failed to delete profile with ID {Id}: Profile not found", id);
            return false;
        }

        public GameProfile ConvertToGameProfile(ClientProfile clientProfile)
        {
            return new GameProfile
            {
                ProfileName = clientProfile.Name,
                InstallDir = clientProfile.InstallDirectory,
                EncryptedUsername = clientProfile.SteamUsername,
                EncryptedPassword = clientProfile.SteamPassword,
                AppID = clientProfile.AppID,
                Arguments = clientProfile.Arguments
            };
        }

        public ClientProfile ConvertToClientProfile(GameProfile gameProfile)
        {
            return new ClientProfile
            {
                Id = _nextId++,
                Name = gameProfile.ProfileName,
                AppID = gameProfile.AppID,
                InstallDirectory = gameProfile.InstallDir,
                SteamUsername = gameProfile.EncryptedUsername,
                SteamPassword = gameProfile.EncryptedPassword,
                Arguments = gameProfile.Arguments,
                ValidateFiles = false,
                AutoRun = false,
                AnonymousLogin = false,
                Status = "Stopped",
                StartTime = DateTime.Now,
                StopTime = DateTime.Now,
                Pid = 0,
                LastRun = DateTime.UtcNow
            };
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);
            using (Aes encryptor = Aes.Create())
            {
                Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(_encryptionKey, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(clearBytes, 0, clearBytes.Length);
                        cs.Close();
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(_encryptionKey, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, 0, cipherBytes.Length);
                            cs.Close();
                        }
                        return Encoding.Unicode.GetString(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decrypting string");
                return "[Không thể giải mã]";
            }
        }

        // Phương thức để tạo tên file duy nhất cho profile
        public string GenerateUniqueFileName(ClientProfile profile)
        {
            // Tạo định danh duy nhất dựa trên tên và AppID
            string safeFileName = $"{profile.Name}_{profile.AppID}";

            // Loại bỏ các ký tự không hợp lệ cho tên file
            safeFileName = string.Join("_", safeFileName.Split(Path.GetInvalidFileNameChars()));

            // Thêm timestamp để đảm bảo độc nhất
            string uniqueFileName = $"{safeFileName}_{DateTime.Now:yyyyMMddHHmmss}.json";

            return uniqueFileName;
        }

        // Phương thức tìm kiếm profile trùng lặp
        public ClientProfile FindDuplicateProfile(ClientProfile profile)
        {
            var allProfiles = GetAllProfiles();
            return allProfiles.FirstOrDefault(p =>
                p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase) &&
                p.AppID.Equals(profile.AppID, StringComparison.OrdinalIgnoreCase));
        }
    }
}