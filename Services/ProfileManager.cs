using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class AppProfileManager
    {
        private readonly string _dataFolder;
        private readonly string _profilesFilePath;
        private readonly string _encryptionKey = "yourEncryptionKey123!@#";
        private static int _nextId = 1;

        public AppProfileManager()
        {
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
            catch (Exception)
            {
                return new List<ClientProfile>();
            }
        }

        public ClientProfile GetProfileById(int id)
        {
            return GetAllProfiles().FirstOrDefault(p => p.Id == id);
        }

        public ClientProfile GetProfileByName(string name)
        {
            return GetAllProfiles().FirstOrDefault(p => p.Name == name);
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
                return true;
            }
            
            return false;
        }

        public bool DeleteProfile(int id)
        {
            var profiles = GetAllProfiles();
            int initialCount = profiles.Count;
            profiles = profiles.Where(p => p.Id != id).ToList();
            
            if (profiles.Count < initialCount)
            {
                SaveProfiles(profiles);
                return true;
            }
            
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
                // Kiểm tra xem chuỗi có phải là chuỗi mã hóa Base64 hợp lệ không
                try {
                    Convert.FromBase64String(cipherText);
                } catch {
                    // Nếu không phải Base64 hợp lệ, trả về chuỗi gốc
                    return cipherText;
                }
                
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
                            try {
                                cs.Write(cipherBytes, 0, cipherBytes.Length);
                                cs.Close();
                                return Encoding.Unicode.GetString(ms.ToArray());
                            } catch {
                                // Nếu xảy ra lỗi khi giải mã, trả về chuỗi gốc
                                return cipherText;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Trả về chuỗi gốc nếu không thể giải mã
                return cipherText;
            }
        }
    }
}