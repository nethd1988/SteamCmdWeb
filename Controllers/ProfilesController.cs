using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProfilesController : ControllerBase
    {
        private readonly ProfileService _profileService;
        private readonly ILogger<ProfilesController> _logger;

        public ProfilesController(
            ProfileService profileService,
            ILogger<ProfilesController> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet]
        public async Task<IActionResult> GetAllProfiles()
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                return Ok(profiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách profiles");
                return StatusCode(500, new { error = "Lỗi khi lấy danh sách profiles" });
            }
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetProfileById(int id)
        {
            try
            {
                var profile = await _profileService.GetProfileByIdAsync(id);
                if (profile == null)
                {
                    return NotFound(new { error = $"Không tìm thấy profile có ID {id}" });
                }
                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy profile có ID {0}", id);
                return StatusCode(500, new { error = "Lỗi khi lấy profile" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    return BadRequest(new { error = "Dữ liệu profile không hợp lệ" });
                }

                var createdProfile = await _profileService.AddProfileAsync(profile);
                return CreatedAtAction(nameof(GetProfileById), new { id = createdProfile.Id }, createdProfile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo profile mới");
                return StatusCode(500, new { error = "Lỗi khi tạo profile mới" });
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateProfile(int id, [FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null || id != profile.Id)
                {
                    return BadRequest(new { error = "Dữ liệu profile không hợp lệ hoặc ID không khớp" });
                }

                var success = await _profileService.UpdateProfileAsync(profile);
                if (!success)
                {
                    return NotFound(new { error = $"Không tìm thấy profile có ID {id}" });
                }

                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật profile có ID {0}", id);
                return StatusCode(500, new { error = "Lỗi khi cập nhật profile" });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteProfile(int id)
        {
            try
            {
                var success = await _profileService.DeleteProfileAsync(id);
                if (!success)
                {
                    return NotFound(new { error = $"Không tìm thấy profile có ID {id}" });
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile có ID {0}", id);
                return StatusCode(500, new { error = "Lỗi khi xóa profile" });
            }
        }

        [HttpGet("client")]
        public async Task<IActionResult> GetAllProfilesForClient()
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                // Nếu muốn trả về đã giải mã, có thể giải mã ở đây hoặc dùng hàm riêng
                // Nếu không muốn trả về password, có thể set null ở đây
                foreach (var profile in profiles)
                {
                    // profile.SteamPassword = null; // Nếu không muốn trả về password
                }
                return Ok(profiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách profiles cho client");
                return StatusCode(500, new { error = "Lỗi khi lấy danh sách profiles cho client" });
            }
        }
    }
}