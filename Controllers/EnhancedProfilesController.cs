using Microsoft.AspNetCore.Authorization;
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
    [Authorize] // Yêu cầu xác thực cho toàn bộ controller
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
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet]
        public IActionResult GetProfiles([FromQuery] bool decrypt = false)
        {
            try
            {
                var profiles = _profileManager.GetAllProfiles();

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

        [HttpPost]
        public IActionResult CreateProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    return BadRequest(new { Error = "Dữ liệu profile không hợp lệ" });
                }

                if (string.IsNullOrEmpty(profile.Name) || string.IsNullOrEmpty(profile.AppID))
                {
                    return BadRequest(new { Error = "Tên và AppID là bắt buộc" });
                }

                if (!profile.AnonymousLogin)
                {
                    profile.SteamUsername = string.IsNullOrEmpty(profile.SteamUsername)
                        ? ""
                        : _decryptionService.EncryptString(profile.SteamUsername);
                    profile.SteamPassword = string.IsNullOrEmpty(profile.SteamPassword)
                        ? ""
                        : _decryptionService.EncryptString(profile.SteamPassword);
                }

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

                    if (!string.IsNullOrEmpty(profile.SteamPassword) &&
                        !profile.SteamPassword.Contains("/") &&
                        !profile.SteamPassword.Contains("="))
                    {
                        profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                    }
                    else if (string.IsNullOrEmpty(profile.SteamPassword))
                    {
                        profile.SteamPassword = existingProfile.SteamPassword;
                    }
                }

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

                var copy = new ClientProfile
                {
                    Id = 0,
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