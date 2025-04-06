using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProfilesController : ControllerBase
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<ProfilesController> _logger;

        public ProfilesController(
            AppProfileManager profileManager,
            ILogger<ProfilesController> logger)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet]
        public IActionResult GetProfiles()
        {
            try
            {
                var profiles = _profileManager.GetAllProfiles();
                _logger.LogInformation("GetProfiles: Trả về {Count} profiles", profiles.Count);
                return Ok(profiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách profiles");
                return StatusCode(500, new { Error = "Lỗi khi lấy danh sách profiles", Message = ex.Message });
            }
        }

        [HttpGet("{id:int}")]
        public IActionResult GetProfileById(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    _logger.LogWarning("GetProfileById: Không tìm thấy profile có ID {Id}", id);
                    return NotFound(new { Error = "Không tìm thấy profile" });
                }
                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Lỗi khi lấy thông tin profile", Message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult CreateProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null || string.IsNullOrEmpty(profile.Name) || string.IsNullOrEmpty(profile.AppID))
                {
                    _logger.LogWarning("CreateProfile: Dữ liệu profile không hợp lệ");
                    return BadRequest(new { Error = "Dữ liệu profile không hợp lệ, cần Name và AppID" });
                }

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
                    _logger.LogWarning("UpdateProfile: Dữ liệu profile không hợp lệ hoặc ID không khớp");
                    return BadRequest(new { Error = "Dữ liệu profile không hợp lệ hoặc ID không khớp" });
                }

                var existingProfile = _profileManager.GetProfileById(id);
                if (existingProfile == null)
                {
                    _logger.LogWarning("UpdateProfile: Không tìm thấy profile có ID {Id}", id);
                    return NotFound(new { Error = "Không tìm thấy profile" });
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
                    _logger.LogWarning("DeleteProfile: Không tìm thấy profile có ID {Id}", id);
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
    }
}