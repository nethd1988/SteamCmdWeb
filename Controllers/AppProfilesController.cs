using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Text.Json;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AppProfilesController : ControllerBase
    {
        private readonly string _dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<AppProfilesController> _logger;

        public AppProfilesController(AppProfileManager profileManager, ILogger<AppProfilesController> logger)
        {
            _profileManager = profileManager;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult GetAppProfiles()
        {
            var profiles = _profileManager.GetAllProfiles();
            _logger.LogInformation("GetAppProfiles: Trả về {Count} profiles", profiles.Count);
            return Ok(profiles);
        }

        [HttpGet("{id:int}")]
        public IActionResult GetAppProfileById(int id)
        {
            var profile = _profileManager.GetProfileById(id);
            if (profile == null)
            {
                _logger.LogWarning("GetAppProfileById: Không tìm thấy profile có ID {Id}", id);
                return NotFound("App Profile not found");
            }

            _logger.LogInformation("GetAppProfileById: Trả về profile {Id}: {Name}", id, profile.Name);
            return Ok(profile);
        }

        [HttpGet("name/{profileName}")]
        public IActionResult GetAppProfileByName(string profileName)
        {
            var profile = _profileManager.GetProfileByName(profileName);
            if (profile == null)
            {
                _logger.LogWarning("GetAppProfileByName: Không tìm thấy profile có tên {Name}", profileName);
                return NotFound("App Profile not found");
            }

            _logger.LogInformation("GetAppProfileByName: Trả về profile {Id}: {Name}", profile.Id, profile.Name);
            return Ok(profile);
        }

        [HttpPost]
        public IActionResult CreateAppProfile([FromBody] ClientProfile profile)
        {
            if (profile == null)
            {
                _logger.LogWarning("CreateAppProfile: Dữ liệu profile không hợp lệ");
                return BadRequest("Invalid profile data");
            }

            _logger.LogInformation("CreateAppProfile: Nhận request thêm profile mới: {Name}, AppID: {AppID}", 
                profile.Name, profile.AppID);

            // Mã hóa thông tin đăng nhập nếu được cung cấp
            if (!string.IsNullOrEmpty(profile.SteamUsername) && !profile.SteamUsername.StartsWith("w5"))
            {
                profile.SteamUsername = _profileManager.EncryptString(profile.SteamUsername);
            }

            if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.SteamPassword.StartsWith("HEQ"))
            {
                profile.SteamPassword = _profileManager.EncryptString(profile.SteamPassword);
            }

            // Thiết lập trạng thái mặc định
            profile.Status = "Ready";
            profile.StartTime = DateTime.Now;
            profile.StopTime = DateTime.Now;
            profile.LastRun = DateTime.UtcNow;
            profile.Pid = 0;

            var result = _profileManager.AddProfile(profile);
            
            _logger.LogInformation("CreateAppProfile: Đã thêm profile mới với ID {Id}", result.Id);
            
            return CreatedAtAction(nameof(GetAppProfileById), new { id = result.Id }, result);
        }

        [HttpPut("{id:int}")]
        public IActionResult UpdateAppProfile(int id, [FromBody] ClientProfile profile)
        {
            if (profile == null || id != profile.Id)
            {
                _logger.LogWarning("UpdateAppProfile: Dữ liệu profile không hợp lệ hoặc ID không khớp");
                return BadRequest("Invalid profile data");
            }

            var existingProfile = _profileManager.GetProfileById(id);
            if (existingProfile == null)
            {
                _logger.LogWarning("UpdateAppProfile: Không tìm thấy profile có ID {Id}", id);
                return NotFound("App Profile not found");
            }

            _logger.LogInformation("UpdateAppProfile: Cập nhật profile ID {Id}: {Name}, AppID: {AppID}", 
                profile.Id, profile.Name, profile.AppID);

            // Mã hóa thông tin đăng nhập nếu được cập nhật
            if (!string.IsNullOrEmpty(profile.SteamUsername) && !profile.SteamUsername.StartsWith("w5"))
            {
                profile.SteamUsername = _profileManager.EncryptString(profile.SteamUsername);
            }

            if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.SteamPassword.StartsWith("HEQ"))
            {
                profile.SteamPassword = _profileManager.EncryptString(profile.SteamPassword);
            }

            // Đảm bảo giữ nguyên các thông tin không được cập nhật
            if (string.IsNullOrEmpty(profile.SteamUsername))
            {
                profile.SteamUsername = existingProfile.SteamUsername;
            }

            if (string.IsNullOrEmpty(profile.SteamPassword))
            {
                profile.SteamPassword = existingProfile.SteamPassword;
            }

            bool updated = _profileManager.UpdateProfile(profile);
            if (!updated)
            {
                _logger.LogWarning("UpdateAppProfile: Không thể cập nhật profile ID {Id}", id);
                return NotFound("Failed to update App Profile");
            }

            _logger.LogInformation("UpdateAppProfile: Đã cập nhật profile ID {Id}", id);
            return Ok(profile);
        }

        [HttpDelete("{id:int}")]
        public IActionResult DeleteAppProfile(int id)
        {
            var profile = _profileManager.GetProfileById(id);
            if (profile == null)
            {
                _logger.LogWarning("DeleteAppProfile: Không tìm thấy profile có ID {Id}", id);
                return NotFound("App Profile not found");
            }

            _logger.LogInformation("DeleteAppProfile: Xóa profile ID {Id}: {Name}", id, profile.Name);

            bool deleted = _profileManager.DeleteProfile(id);
            if (!deleted)
            {
                _logger.LogWarning("DeleteAppProfile: Không thể xóa profile ID {Id}", id);
                return NotFound("App Profile not found");
            }

            _logger.LogInformation("DeleteAppProfile: Đã xóa profile ID {Id}", id);
            return NoContent();
        }

        [HttpGet("{id:int}/decrypt")]
        public IActionResult GetDecryptedCredentials(int id)
        {
            var profile = _profileManager.GetProfileById(id);
            if (profile == null)
            {
                _logger.LogWarning("GetDecryptedCredentials: Không tìm thấy profile có ID {Id}", id);
                return NotFound("App Profile not found");
            }

            _logger.LogInformation("GetDecryptedCredentials: Yêu cầu giải mã thông tin đăng nhập cho profile ID {Id}", id);

            // Bỏ kiểm tra localhost để cho phép truy cập từ bất kỳ đâu
            // if (!Request.Host.Host.Equals("localhost") && !Request.Host.Host.Equals("127.0.0.1"))
            //    return Forbid("This operation is only allowed from localhost");

            var credentials = new
            {
                Username = _profileManager.DecryptString(profile.SteamUsername),
                Password = _profileManager.DecryptString(profile.SteamPassword)
            };

            return Ok(credentials);
        }

        [HttpGet("backup")]
        public async Task<IActionResult> BackupProfiles()
        {
            try
            {
                var profiles = _profileManager.GetAllProfiles();
                
                if (profiles.Count == 0)
                {
                    _logger.LogWarning("BackupProfiles: Không có profiles để backup");
                    return NotFound(new { Success = false, Message = "Không có profiles để backup" });
                }

                // Tạo thư mục backup nếu chưa tồn tại
                string backupFolder = Path.Combine(_dataFolder, "Backup");
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                // Tạo tên file backup
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"manual_backup_{timestamp}.json";
                string filePath = Path.Combine(backupFolder, fileName);

                // Chuyển đổi và ghi file
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonContent = JsonSerializer.Serialize(profiles, options);
                await System.IO.File.WriteAllTextAsync(filePath, jsonContent);

                _logger.LogInformation("BackupProfiles: Đã backup {Count} profiles vào file {FilePath}", 
                    profiles.Count, filePath);

                return Ok(new { 
                    Success = true, 
                    Message = $"Đã sao lưu {profiles.Count} profiles vào file {fileName}",
                    FileName = fileName,
                    FilePath = filePath,
                    Count = profiles.Count,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BackupProfiles: Lỗi khi backup profiles");
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }
    }
}