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
    public class AppProfileManager
    {
        private readonly string _dataFolder;
        private readonly string _profilesFilePath;
        private readonly string _encryptionKey = "yourEncryptionKey123!@#";
        private static int _nextId = 1;
        private readonly ILogger<AppProfileManager> _logger;

        public AppProfileManager(ILogger<AppProfileManager> logger = null)
        {
            _logger = logger;
            _dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(_dataFolder))
            {
                Directory.CreateDirectory(_dataFolder);
                _logger?.LogInformation("Tạo thư mục Data tại {Path}", _dataFolder);
            }
            _profilesFilePath = Path.Combine(_dataFolder, "profiles.json");
            
            // Initialize the next ID based on existing profiles
            var profiles = GetAllProfiles();
            if (profiles.Any())
            {
                _nextId = profiles.Max(p => p.Id) + 1;
                _logger?.LogInformation("Khởi tạo ID tiếp theo: {NextId}", _nextId);
            }
        }

        public List<ClientProfile> GetAllProfiles()
        {
            if (!File.Exists(_profilesFilePath))
            {
                _logger?.LogInformation("File profiles.json không tồn tại, trả về danh sách trống");
                return new List<ClientProfile>();
            }

            try
            {
                string jsonContent = File.ReadAllText(_profilesFilePath);
                var options = new JsonSerializerOptions { 
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(jsonContent, options) ?? new List<ClientProfile>();
                
                // Đảm bảo tất cả các profile có giá trị hợp lệ
                foreach (var profile in profiles)
                {
                    if (string.IsNullOrEmpty(profile.Name))
                    {
                        profile.Name = $"Game {profile.Id}";
                    }
                    
                    if (string.IsNullOrEmpty(profile.AppID))
                    {
                        profile.AppID = "000000";
                    }
                    
                    if (string.IsNullOrEmpty(profile.Status))
                    {
                        profile.Status = "Ready";
                    }
                }
                
                _logger?.LogInformation("Đã tải {Count} profiles từ file", profiles.Count);
                return profiles;
            }
            catch (JsonException ex)
            {
                _logger?.LogError(ex, "Lỗi phân tích JSON từ file profiles.json");
                return new List<ClientProfile>();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi đọc profiles");
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
            {
                return null;
            }
            
            return GetAllProfiles().FirstOrDefault(p => p.Name == name);
        }

        public void SaveProfiles(List<ClientProfile> profiles)
        {
            // Sao lưu file cũ trước khi ghi đè
            if (File.Exists(_profilesFilePath))
            {
                try
                {
                    string backupFolder = Path.Combine(_dataFolder, "Backup");
                    if (!Directory.Exists(backupFolder))
                    {
                        Directory.CreateDirectory(backupFolder);
                    }
                    
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backupFileName = $"profiles_{timestamp}.json";
                    string backupFilePath = Path.Combine(backupFolder, backupFileName);
                    
                    File.Copy(_profilesFilePath, backupFilePath);
                    _logger?.LogInformation("Đã sao lưu file profiles.json vào {BackupPath}", backupFilePath);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Không thể sao lưu file profiles.json");
                }
            }
        
            try
            {
                var options = new JsonSerializerOptions { 
                    WriteIndented = true,
                    PropertyNamingPolicy = null // Giữ nguyên tên thuộc tính
                };
                string jsonContent = JsonSerializer.Serialize(profiles, options);
                File.WriteAllText(_profilesFilePath, jsonContent);
                _logger?.LogInformation("Đã lưu {Count} profiles vào file", profiles.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi lưu profiles vào file");
                throw; // Ném lại exception để controller xử lý
            }
        }

        public ClientProfile AddProfile(ClientProfile profile)
        {
            var profiles = GetAllProfiles();
            
            // Đảm bảo ID duy nhất
            if (profile.Id <= 0)
            {
                profile.Id = _nextId++;
                _logger?.LogInformation("Gán ID mới cho profile: {Id}", profile.Id);
            }
            else if (profiles.Any(p => p.Id == profile.Id))
            {
                // Nếu ID đã tồn tại, tạo ID mới
                profile.Id = _nextId++;
                _logger?.LogInformation("ID đã tồn tại, gán ID mới: {Id}", profile.Id);
            }
            
            // Đảm bảo các trường bắt buộc có giá trị
            if (string.IsNullOrEmpty(profile.Name))
            {
                profile.Name = $"Game {profile.Id}";
            }
            
            if (string.IsNullOrEmpty(profile.AppID))
            {
                profile.AppID = "000000";
            }
            
            // Set giá trị mặc định
            profile.StartTime = DateTime.Now;
            profile.StopTime = DateTime.Now;
            profile.LastRun = DateTime.UtcNow;
            profile.Status = profile.Status ?? "Ready";
            profile.Pid = 0;

            profiles.Add(profile);
            SaveProfiles(profiles);
            
            _logger?.LogInformation("Đã thêm profile mới: {Id} - {Name}", profile.Id, profile.Name);
            
            return profile;
        }

        public bool UpdateProfile(ClientProfile updatedProfile)
        {
            var profiles = GetAllProfiles();
            int index = profiles.FindIndex(p => p.Id == updatedProfile.Id);
            
            if (index >= 0)
            {
                // Đảm bảo các trường bắt buộc có giá trị
                if (string.IsNullOrEmpty(updatedProfile.Name))
                {
                    updatedProfile.Name = $"Game {updatedProfile.Id}";
                }
                
                if (string.IsNullOrEmpty(updatedProfile.AppID))
                {
                    updatedProfile.AppID = "000000";
                }
                
                if (string.IsNullOrEmpty(updatedProfile.Status))
                {
                    updatedProfile.Status = "Ready";
                }
                
                profiles[index] = updatedProfile;
                SaveProfiles(profiles);
                
                _logger?.LogInformation("Đã cập nhật profile: {Id} - {Name}", updatedProfile.Id, updatedProfile.Name);
                
                return true;
            }
            
            _logger?.LogWarning("Không tìm thấy profile có ID {Id} để cập nhật", updatedProfile.Id);
            
            return false;
        }

        public bool DeleteProfile(int id)
        {
            var profiles = GetAllProfiles();
            int initialCount = profiles.Count;
            var profileToDelete = profiles.FirstOrDefault(p => p.Id == id);
            
            if (profileToDelete != null)
            {
                _logger?.LogInformation("Xóa profile: {Id} - {Name}", profileToDelete.Id, profileToDelete.Name);
            }
            
            profiles = profiles.Where(p => p.Id != id).ToList();
            
            if (profiles.Count < initialCount)
            {
                SaveProfiles(profiles);
                return true;
            }
            
            _logger?.LogWarning("Không tìm thấy profile có ID {Id} để xóa", id);
            
            return false;
        }

        public GameProfile ConvertToGameProfile(ClientProfile clientProfile)
        {
            _logger?.LogInformation("Chuyển đổi ClientProfile sang GameProfile: {Id} - {Name}", 
                clientProfile.Id, clientProfile.Name);
                
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
            _logger?.LogInformation("Chuyển đổi GameProfile sang ClientProfile: {Name}", gameProfile.ProfileName);
            
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
                Status = "Ready",
                StartTime = DateTime.Now,
                StopTime = DateTime.Now,
                Pid = 0,
                LastRun = DateTime.UtcNow
            };
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            
            _logger?.LogDebug("Mã hóa chuỗi");
            
            try
            {
                byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(_encryptionKey, 
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
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
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi mã hóa chuỗi");
                return plainText; // Trả về chuỗi gốc nếu có lỗi
            }
        }

        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            
            _logger?.LogDebug("Giải mã chuỗi");
            
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
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(_encryptionKey, 
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
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
                _logger?.LogWarning("Không thể giải mã chuỗi, trả về nguyên bản");
                return cipherText;
            }
        }
        
        // Lấy thống kê profiles
        public object GetProfileStats()
        {
            var profiles = GetAllProfiles();
            
            return new
            {
                TotalCount = profiles.Count,
                ActiveCount = profiles.Count(p => p.Status == "Running"),
                ReadyCount = profiles.Count(p => p.Status == "Ready"),
                AnonymousCount = profiles.Count(p => p.AnonymousLogin),
                LastUpdated = DateTime.Now,
                GamesWithCredentials = profiles.Count(p => !string.IsNullOrEmpty(p.SteamUsername)),
                TopGames = profiles.Take(5).Select(p => new { p.Id, p.Name, p.AppID }).ToList()
            };
        }
    }
}