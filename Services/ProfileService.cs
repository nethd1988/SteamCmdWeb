using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class ProfileService
    {
        private readonly string _profilesPath;
        private readonly ILogger<ProfileService> _logger;
        private readonly DecryptionService _decryptionService;
        private readonly object _fileLock = new object(); // Khóa để xử lý đồng thời

        public ProfileService(
            ILogger<ProfileService> logger,
            DecryptionService decryptionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));

            var currentDir = AppDomain.CurrentDomain.BaseDirectory;
            // Lưu file profiles.json trong thư mục data
            string dataDir = Path.Combine(currentDir, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
                logger.LogInformation("Đã tạo thư mục data tại {0}", dataDir);
            }

            _profilesPath = Path.Combine(dataDir, "profiles.json");

            // Tạo file profiles.json trống nếu chưa tồn tại
            if (!File.Exists(_profilesPath))
            {
                File.WriteAllText(_profilesPath, "[]");
                logger.LogInformation("Đã tạo file profiles.json trống tại {0}", _profilesPath);
            }
        }

        public async Task<List<ClientProfile>> GetAllProfilesAsync()
        {
            if (!File.Exists(_profilesPath))
            {
                _logger.LogInformation("File profiles.json không tồn tại tại {0}. Trả về danh sách rỗng.", _profilesPath);
                return new List<ClientProfile>();
            }

            try
            {
                string json = await File.ReadAllTextAsync(_profilesPath);
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ClientProfile>();
                _logger.LogInformation("Đã đọc {0} profiles từ {1}", profiles.Count, _profilesPath);
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc file profiles.json tại {0}", _profilesPath);
                throw new Exception($"Không thể đọc file profiles.json: {ex.Message}", ex);
            }
        }

        public async Task<ClientProfile> GetProfileByIdAsync(int id)
        {
            try
            {
                var profiles = await GetAllProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Id == id);
                if (profile == null)
                {
                    _logger.LogWarning("Không tìm thấy profile với ID {0}", id);
                }
                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy profile với ID {0}", id);
                throw;
            }
        }

        public async Task<List<ClientProfile>> GetAllDecryptedProfilesAsync()
        {
            var profiles = await GetAllProfilesAsync();

            foreach (var profile in profiles)
            {
                // Giải mã thông tin tài khoản và mật khẩu
                if (!string.IsNullOrEmpty(profile.SteamUsername))
                {
                    try
                    {
                        profile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi giải mã tên đăng nhập cho profile {ProfileId}", profile.Id);
                    }
                }

                if (!string.IsNullOrEmpty(profile.SteamPassword))
                {
                    try
                    {
                        profile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi giải mã mật khẩu cho profile {ProfileId}", profile.Id);
                    }
                }
            }

            return profiles;
        }

        public async Task<ClientProfile> GetDecryptedProfileByIdAsync(int id)
        {
            var profile = await GetProfileByIdAsync(id);
            if (profile == null)
            {
                return null;
            }

            // Giải mã thông tin tài khoản và mật khẩu
            if (!string.IsNullOrEmpty(profile.SteamUsername))
            {
                try
                {
                    profile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi giải mã tên đăng nhập cho profile {ProfileId}", profile.Id);
                }
            }

            if (!string.IsNullOrEmpty(profile.SteamPassword))
            {
                try
                {
                    profile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi giải mã mật khẩu cho profile {ProfileId}", profile.Id);
                }
            }

            return profile;
        }

        public async Task SaveProfiles(List<ClientProfile> profiles)
        {
            int retryCount = 0;
            int maxRetries = 5;
            int retryDelayMs = 200;

            while (retryCount < maxRetries)
            {
                try
                {
                    var directory = Path.GetDirectoryName(_profilesPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        _logger.LogInformation("Đã tạo thư mục {0}", directory);
                    }

                    string updatedJson = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });

                    // Sử dụng FileMode.Create để tạo mới file mỗi lần ghi
                    using (var fileStream = new FileStream(_profilesPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(fileStream))
                    {
                        await writer.WriteAsync(updatedJson);
                    }

                    _logger.LogInformation("Đã lưu {0} profiles vào {1}", profiles.Count, _profilesPath);
                    return;
                }
                catch (IOException ex) when (ex.Message.Contains("being used") || ex.Message.Contains("access") || ex.HResult == -2147024864)
                {
                    retryCount++;
                    _logger.LogWarning("Lần thử {0}/{1}: Không thể truy cập file profiles.json, đang chờ {2}ms",
                        retryCount, maxRetries, retryDelayMs);

                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(retryDelayMs);
                        retryDelayMs *= 2; // Tăng thời gian chờ theo cấp số nhân
                    }
                    else
                    {
                        _logger.LogError("Không thể lưu profiles.json sau {0} lần thử: {1}", maxRetries, ex.Message);
                        throw new Exception($"Không thể lưu file profiles.json sau nhiều lần thử: {ex.Message}", ex);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi lưu profiles vào {0}", _profilesPath);
                    throw new Exception($"Không thể lưu file profiles.json: {ex.Message}", ex);
                }
            }
        }

        public async Task<ClientProfile> AddProfileAsync(ClientProfile profile)
        {
            try
            {
                _logger.LogInformation("Bắt đầu thêm profile: {Name}", profile.Name);
                var profiles = await GetAllProfilesAsync();

                // Kiểm tra xem AppID đã tồn tại chưa
                if (profiles.Any(p => p.AppID == profile.AppID))
                {
                    _logger.LogWarning("AppID {AppID} đã tồn tại trong hệ thống", profile.AppID);
                    throw new Exception($"AppID {profile.AppID} đã tồn tại trong hệ thống");
                }

                // Tạo ID mới
                int newId = profiles.Count > 0 ? profiles.Max(p => p.Id) + 1 : 1;
                profile.Id = newId;

                // Mã hóa thông tin đăng nhập nếu có
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

                // Thiết lập các giá trị mặc định
                if (string.IsNullOrEmpty(profile.Status))
                {
                    profile.Status = "Ready";
                }

                if (profile.StartTime == default)
                {
                    profile.StartTime = DateTime.Now;
                }

                if (profile.StopTime == default)
                {
                    profile.StopTime = DateTime.Now;
                }

                if (profile.LastRun == default)
                {
                    profile.LastRun = DateTime.UtcNow;
                }

                profiles.Add(profile);
                await SaveProfiles(profiles);
                _logger.LogInformation("Đã thêm profile thành công: {Name} với ID {Id}", profile.Name, profile.Id);

                return profile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm profile mới: {Name}", profile.Name);
                throw;
            }
        }

        public async Task<bool> DeleteProfileAsync(int id)
        {
            try
            {
                _logger.LogInformation("Bắt đầu xóa profile với ID {0}", id);
                var profiles = await GetAllProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Id == id);
                if (profile == null)
                {
                    _logger.LogWarning("Không tìm thấy profile với ID {0} để xóa", id);
                    return false;
                }

                profiles.Remove(profile);
                await SaveProfiles(profiles);
                _logger.LogInformation("Đã xóa profile với ID {0}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile với ID {0}", id);
                throw;
            }
        }

        public async Task<bool> UpdateProfileAsync(ClientProfile updatedProfile)
        {
            try
            {
                _logger.LogInformation("Bắt đầu cập nhật profile với ID {0}", updatedProfile.Id);
                var profiles = await GetAllProfilesAsync();
                int index = profiles.FindIndex(p => p.Id == updatedProfile.Id);
                if (index == -1)
                {
                    _logger.LogWarning("Không tìm thấy profile với ID {0} để cập nhật", updatedProfile.Id);
                    return false;
                }

                // Mã hóa thông tin đăng nhập nếu có thay đổi và không phải đăng nhập ẩn danh
                if (!updatedProfile.AnonymousLogin)
                {
                    var existingProfile = profiles[index];

                    // Kiểm tra xem thông tin đăng nhập có thay đổi không
                    if (updatedProfile.SteamUsername != existingProfile.SteamUsername &&
                        !string.IsNullOrEmpty(updatedProfile.SteamUsername))
                    {
                        // Kiểm tra xem đã được mã hóa chưa
                        try
                        {
                            // Thử giải mã để xem có phải chuỗi đã mã hóa không
                            _decryptionService.DecryptString(updatedProfile.SteamUsername);
                        }
                        catch
                        {
                            // Nếu không giải mã được, coi như là chuỗi thô và mã hóa
                            updatedProfile.SteamUsername = _decryptionService.EncryptString(updatedProfile.SteamUsername);
                        }
                    }

                    if (updatedProfile.SteamPassword != existingProfile.SteamPassword &&
                        !string.IsNullOrEmpty(updatedProfile.SteamPassword))
                    {
                        // Kiểm tra xem đã được mã hóa chưa
                        try
                        {
                            // Thử giải mã để xem có phải chuỗi đã mã hóa không
                            _decryptionService.DecryptString(updatedProfile.SteamPassword);
                        }
                        catch
                        {
                            // Nếu không giải mã được, coi như là chuỗi thô và mã hóa
                            updatedProfile.SteamPassword = _decryptionService.EncryptString(updatedProfile.SteamPassword);
                        }
                    }
                }
                else
                {
                    // Nếu là đăng nhập ẩn danh, xóa thông tin đăng nhập
                    updatedProfile.SteamUsername = "";
                    updatedProfile.SteamPassword = "";
                }

                profiles[index] = updatedProfile;
                await SaveProfiles(profiles);
                _logger.LogInformation("Đã cập nhật profile thành công với ID {0}", updatedProfile.Id);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật profile với ID {0}", updatedProfile.Id);
                throw;
            }
        }
    }
}