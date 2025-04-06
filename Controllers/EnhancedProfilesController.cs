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
    [Route("api/[controller]")]
    [ApiController]
    public class EnhancedProfilesController : ControllerBase
    {
        private readonly AppProfileManager _profileManager;
        private readonly DecryptionService _decryptionService;
        private readonly ILogger<EnhancedProfilesController> _logger;

        public EnhancedProfilesController(
            AppProfileManager profileManager,
            DecryptionService decryptionService,
            ILogger<EnhancedProfilesController> logger)
        {
            _profileManager = profileManager;
            _decryptionService = decryptionService;
            _logger = logger;
        }

        /// <summary>
        /// Lấy tất cả profiles với tùy chọn giải mã
        /// </summary>
        [HttpGet]
        public IActionResult GetProfiles([FromQuery] bool decrypt = false)
        {
            try
            {
                var profiles = _profileManager.GetAllProfiles();

                if (decrypt)
                {
                    // Thêm thông tin giải mã (chỉ để hiển thị, không lưu trong profiles)
                    var enhancedProfiles = profiles.Select(p => new
                    {
                        Profile = p,
                        DecryptedInfo = p.AnonymousLogin ? null : new
                        {
                            Username = _decryptionService.DecryptString(p.SteamUsername),
                            Password = _decryptionService.DecryptString(p.SteamPassword)
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
        /// Lấy profile theo ID với tùy chọn giải mã
        /// </summary>
        [HttpGet("{id:int}")]
        public IActionResult GetProfileById(int id, [FromQuery] bool decrypt = false)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                if (decrypt && !profile.AnonymousLogin)
                {
                    var result = new
                    {
                        Profile = profile,
                        DecryptedInfo = new
                        {
                            Username = _decryptionService.DecryptString(profile.SteamUsername),
                            Password = _decryptionService.DecryptString(profile.SteamPassword)
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
        /// Lấy thông tin đăng nhập đã giải mã cho profile
        /// </summary>
        [HttpGet("{id:int}/credentials")]
        public IActionResult GetDecryptedCredentials(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                // Kiểm tra nếu người dùng đang kết nối từ mạng nội bộ
                var isLocalRequest = Request.HttpContext.Connection.RemoteIpAddress?.IsLoopback ?? false;
                if (!isLocalRequest)
                {
                    _logger.LogWarning("Yêu cầu truy cập thông tin đăng nhập từ IP không an toàn: {IP}",
                        Request.HttpContext.Connection.RemoteIpAddress);
                }

                if (profile.AnonymousLogin)
                {
                    return Ok(new { Message = "Profile này sử dụng đăng nhập ẩn danh (không có thông tin đăng nhập)" });
                }

                var credentials = new
                {
                    Username = _decryptionService.DecryptString(profile.SteamUsername),
                    Password = _decryptionService.DecryptString(profile.SteamPassword)
                };

                return Ok(credentials);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thông tin đăng nhập cho profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Lỗi khi giải mã thông tin đăng nhập", Message = ex.Message });
            }
        }

        /// <summary>
        /// Tạo profile mới với mã hóa đúng cách
        /// </summary>
        [HttpPost]
        public IActionResult CreateProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    return BadRequest(new { Error = "Dữ liệu profile không hợp lệ" });
                }

                // Xác thực các trường bắt buộc
                if (string.IsNullOrEmpty(profile.Name) || string.IsNullOrEmpty(profile.AppID))
                {
                    return BadRequest(new { Error = "Tên và AppID là bắt buộc" });
                }

                // Mã hóa thông tin đăng nhập nếu không phải đăng nhập ẩn danh
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

                // Thiết lập giá trị mặc định
                profile.Status = "Ready";
                profile.StartTime = DateTime.Now;
                profile.StopTime = DateTime.Now;
                profile.LastRun = DateTime.UtcNow;
                profile.Pid = 0;

                var result = _profileManager.AddProfile(profile);
                return CreatedAtAction(nameof(GetProfileById), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo profile mới");
                return StatusCode(500, new { Error = "Lỗi khi tạo profile", Message = ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật profile với mã hóa đúng cách
        /// </summary>
        [HttpPut("{id:int}")]
        public IActionResult UpdateProfile(int id, [FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null || id != profile.Id)
                {
                    return BadRequest(new { Error = "Dữ liệu profile không hợp lệ hoặc ID không khớp" });
                }

                var existingProfile = _profileManager.GetProfileById(id);
                if (existingProfile == null)
                {
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                // Mã hóa thông tin đăng nhập nếu không phải đăng nhập ẩn danh và thông tin được cập nhật
                if (!profile.AnonymousLogin)
                {
                    // Chỉ mã hóa nếu chuỗi có vẻ là văn bản thô (không phải đã mã hóa)
                    if (!string.IsNullOrEmpty(profile.SteamUsername) &&
                        !profile.SteamUsername.Contains("/") &&
                        !profile.SteamUsername.Contains("="))
                    {
                        profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                    }
                    else if (string.IsNullOrEmpty(profile.SteamUsername))
                    {
                        // Giữ nguyên username cũ nếu không được cung cấp
                        profile.SteamUsername = existingProfile.SteamUsername;
                    }

                    if (!string.IsNullOrEmpty(profile.SteamPassword) &&
                        !profile.SteamPassword.Contains("/") &&
                        !profile.SteamPassword.Contains("="))
                    {
                        profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                    }
                    else if (string.IsNullOrEmpty(profile.SteamPassword))
                    {
                        // Giữ nguyên password cũ nếu không được cung cấp
                        profile.SteamPassword = existingProfile.SteamPassword;
                    }
                }

                // Đảm bảo cập nhật đúng thông tin
                if (string.IsNullOrEmpty(profile.Status))
                {
                    profile.Status = existingProfile.Status;
                }

                bool updated = _profileManager.UpdateProfile(profile);
                if (!updated)
                {
                    return NotFound(new { Error = "Không thể cập nhật profile" });
                }

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
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                bool deleted = _profileManager.DeleteProfile(id);
                if (!deleted)
                {
                    return NotFound(new { Error = "Không thể xóa profile" });
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Lỗi khi xóa profile", Message = ex.Message });
            }
        }

        /// <summary>
        /// Tạo bản sao profile
        /// </summary>
        [HttpPost("{id:int}/duplicate")]
        public IActionResult DuplicateProfile(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }

                // Tạo bản sao của profile
                var copy = new ClientProfile
                {
                    Id = 0, // Sẽ được gán bởi AppProfileManager
                    Name = $"{profile.Name} (Bản sao)",
                    AppID = profile.AppID,
                    InstallDirectory = profile.InstallDirectory,
                    SteamUsername = profile.SteamUsername,
                    SteamPassword = profile.SteamPassword,
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
                return CreatedAtAction(nameof(GetProfileById), new { id = result.Id }, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo bản sao profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Lỗi khi tạo bản sao profile", Message = ex.Message });
            }
        }
    }
}