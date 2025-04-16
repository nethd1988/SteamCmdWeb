using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SteamCmdWeb.Controllers
{
    [Route("api/profiles")]
    [ApiController]
    public class ProfilesController : ControllerBase
    {
        private readonly AppProfileManager _profileManager;
        private readonly DecryptionService _decryptionService;
        private readonly ILogger<ProfilesController> _logger;

        public ProfilesController(
            AppProfileManager profileManager,
            DecryptionService decryptionService,
            ILogger<ProfilesController> logger)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Lấy danh sách tất cả profiles
        /// </summary>
        [HttpGet]
        public IActionResult GetProfiles([FromQuery] bool decrypt = false)
        {
            try
            {
                var profiles = _profileManager.GetAllProfiles();
                _logger.LogInformation("GetProfiles: Trả về {Count} profiles, decrypt={Decrypt}", profiles.Count, decrypt);

                if (decrypt)
                {
                    var enhancedProfiles = profiles.Select(p => new
                    {
                        Profile = p,
                        DecryptedInfo = p.AnonymousLogin
                            ? new { Username = "Anonymous", Password = "N/A" }
                            : new
                            {
                                Username = _decryptionService.DecryptString(p.SteamUsername),
                                Password = "***HIDDEN***" // Không trả về mật khẩu
                            }
                    }).ToList();
                    return Ok(enhancedProfiles);
                }

                return Ok(profiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách profiles với decrypt={Decrypt}", decrypt);
                return StatusCode(500, new { Error = "Lỗi khi lấy danh sách profiles", Message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy profile theo ID
        /// </summary>
        [HttpGet("{id:int}")]
        public IActionResult GetProfileById(int id, [FromQuery] bool decrypt = false)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    _logger.LogWarning("GetProfileById: Không tìm thấy profile có ID {Id}", id);
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                _logger.LogInformation("GetProfileById: Trả về profile {Id}: {Name}", id, profile.Name);

                if (decrypt && !profile.AnonymousLogin)
                {
                    var result = new
                    {
                        Profile = profile,
                        DecryptedInfo = new
                        {
                            Username = _decryptionService.DecryptString(profile.SteamUsername),
                            Password = "***HIDDEN***" // Không trả về mật khẩu
                        }
                    };
                    return Ok(result);
                }

                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy profile {ProfileId} với decrypt={Decrypt}", id, decrypt);
                return StatusCode(500, new { Error = "Lỗi khi lấy thông tin profile", Message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy profile theo tên
        /// </summary>
        [HttpGet("name/{profileName}")]
        public IActionResult GetProfileByName(string profileName, [FromQuery] bool decrypt = false)
        {
            try
            {
                var profile = _profileManager.GetProfileByName(profileName);
                if (profile == null)
                {
                    _logger.LogWarning("GetProfileByName: Không tìm thấy profile có tên {Name}", profileName);
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                _logger.LogInformation("GetProfileByName: Trả về profile {Id}: {Name}", profile.Id, profile.Name);

                if (decrypt && !profile.AnonymousLogin)
                {
                    var result = new
                    {
                        Profile = profile,
                        DecryptedInfo = new
                        {
                            Username = _decryptionService.DecryptString(profile.SteamUsername),
                            Password = "***HIDDEN***" // Không trả về mật khẩu
                        }
                    };
                    return Ok(result);
                }

                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy profile có tên {ProfileName}", profileName);
                return StatusCode(500, new { Error = "Lỗi khi lấy thông tin profile", Message = ex.Message });
            }
        }

        /// <summary>
        /// Tạo profile mới
        /// </summary>
        [HttpPost]
        public IActionResult CreateProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    _logger.LogWarning("CreateProfile: Dữ liệu profile không hợp lệ");
                    return BadRequest(new { Error = "Dữ liệu profile không hợp lệ" });
                }

                if (string.IsNullOrEmpty(profile.Name) || string.IsNullOrEmpty(profile.AppID))
                {
                    _logger.LogWarning("CreateProfile: Tên và AppID là bắt buộc");
                    return BadRequest(new { Error = "Tên và AppID là bắt buộc" });
                }

                _logger.LogInformation("CreateProfile: Nhận request thêm profile mới: {Name}, AppID: {AppID}",
                    profile.Name, profile.AppID);

                // Mã hóa thông tin đăng nhập nếu không phải đăng nhập ẩn danh
                if (!profile.AnonymousLogin)
                {
                    if (!string.IsNullOrEmpty(profile.SteamUsername) &&
                        !profile.SteamUsername.Contains("/") &&
                        !profile.SteamUsername.Contains("="))
                    {
                        profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                    }

                    // Bỏ qua mật khẩu - không lưu
                    profile.SteamPassword = "";
                }

                // Thiết lập trạng thái mặc định
                profile.Status = "Ready";
                profile.StartTime = DateTime.Now;
                profile.StopTime = DateTime.Now;
                profile.LastRun = DateTime.UtcNow;
                profile.Pid = 0;

                var result = _profileManager.AddProfile(profile);

                _logger.LogInformation("CreateProfile: Đã thêm profile mới với ID {Id}", result.Id);

                return CreatedAtAction(nameof(GetProfileById), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo profile mới");
                return StatusCode(500, new { Error = "Lỗi khi tạo profile", Message = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật profile
        /// </summary>
        [HttpPut("{id:int}")]
        public IActionResult UpdateProfile(int id, [FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null || id != profile.Id)
                {
                    _logger.LogWarning("UpdateProfile: Dữ liệu profile không hợp lệ hoặc ID không khớp");
                    return BadRequest(new { Error = "Dữ liệu profile không hợp lệ hoặc ID không khớp" });
                }

                var existingProfile = _profileManager.GetProfileById(id);
                if (existingProfile == null)
                {
                    _logger.LogWarning("UpdateProfile: Không tìm thấy profile có ID {Id}", id);
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                _logger.LogInformation("UpdateProfile: Cập nhật profile ID {Id}: {Name}, AppID: {AppID}",
                    profile.Id, profile.Name, profile.AppID);

                // Mã hóa thông tin đăng nhập nếu không phải đăng nhập ẩn danh
                if (!profile.AnonymousLogin)
                {
                    if (!string.IsNullOrEmpty(profile.SteamUsername) &&
                        !profile.SteamUsername.Contains("/") &&
                        !profile.SteamUsername.Contains("="))
                    {
                        profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                    }
                    else if (string.IsNullOrEmpty(profile.SteamUsername))
                    {
                        profile.SteamUsername = existingProfile.SteamUsername;
                    }

                    // Giữ lại mật khẩu cũ
                    profile.SteamPassword = existingProfile.SteamPassword;
                }

                // Giữ nguyên status nếu không được cập nhật
                if (string.IsNullOrEmpty(profile.Status))
                {
                    profile.Status = existingProfile.Status;
                }

                bool updated = _profileManager.UpdateProfile(profile);
                if (!updated)
                {
                    _logger.LogWarning("UpdateProfile: Không thể cập nhật profile ID {Id}", id);
                    return NotFound(new { Error = "Không thể cập nhật profile" });
                }

                _logger.LogInformation("UpdateProfile: Đã cập nhật profile ID {Id}", id);
                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Lỗi khi cập nhật profile", Message = ex.Message });
            }
        }

        /// <summary>
        /// Xóa profile
        /// </summary>
        [HttpDelete("{id:int}")]
        public IActionResult DeleteProfile(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    _logger.LogWarning("DeleteProfile: Không tìm thấy profile có ID {Id}", id);
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                _logger.LogInformation("DeleteProfile: Xóa profile ID {Id}: {Name}", id, profile.Name);

                bool deleted = _profileManager.DeleteProfile(id);
                if (!deleted)
                {
                    _logger.LogWarning("DeleteProfile: Không thể xóa profile ID {Id}", id);
                    return NotFound(new { Error = "Không thể xóa profile" });
                }

                _logger.LogInformation("DeleteProfile: Đã xóa profile ID {Id}", id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Lỗi khi xóa profile", Message = ex.Message });
            }
        }

        /// <summary>
        /// Sao chép profile
        /// </summary>
        [HttpPost("{id:int}/duplicate")]
        public IActionResult DuplicateProfile(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    _logger.LogWarning("DuplicateProfile: Không tìm thấy profile có ID {Id}", id);
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                var copy = new ClientProfile
                {
                    Id = 0,
                    Name = $"{profile.Name} (Bản sao)",
                    AppID = profile.AppID,
                    InstallDirectory = profile.InstallDirectory,
                    SteamUsername = profile.SteamUsername,
                    SteamPassword = profile.SteamPassword, // Giữ lại mật khẩu đã mã hóa
                    Arguments = profile.Arguments,
                    ValidateFiles = profile.ValidateFiles,
                    AutoRun = profile.AutoRun,
                    AnonymousLogin = profile.AnonymousLogin,
                    Status = "Ready",
                    StartTime = DateTime.Now,
                    StopTime = DateTime.Now,
                    LastRun = DateTime.UtcNow,
                    Pid = 0
                };

                var result = _profileManager.AddProfile(copy);
                _logger.LogInformation("DuplicateProfile: Đã tạo bản sao của profile {Id} với ID mới {NewId}", id, result.Id);
                return CreatedAtAction(nameof(GetProfileById), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo bản sao profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Lỗi khi tạo bản sao profile", Message = ex.Message });
            }
        }

        /// <summary>
        /// Sao lưu tất cả profiles
        /// </summary>
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
                string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backup");
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                // Tạo tên file backup
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"manual_backup_{timestamp}.json";
                string filePath = Path.Combine(backupFolder, fileName);

                // Chuyển đổi và ghi file
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string jsonContent = System.Text.Json.JsonSerializer.Serialize(profiles, options);
                await System.IO.File.WriteAllTextAsync(filePath, jsonContent);

                _logger.LogInformation("BackupProfiles: Đã backup {Count} profiles vào file {FilePath}",
                    profiles.Count, filePath);

                return Ok(new
                {
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