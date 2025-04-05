using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System.IO;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AppProfilesController : ControllerBase
    {
        private readonly string _dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        private readonly AppProfileManager _profileManager;

        public AppProfilesController(AppProfileManager profileManager)
        {
            _profileManager = profileManager;
        }

        [HttpGet]
        public IActionResult GetAppProfiles()
        {
            var profiles = _profileManager.GetAllProfiles();
            return Ok(profiles);
        }

        [HttpGet("{id:int}")]
        public IActionResult GetAppProfileById(int id)
        {
            var profile = _profileManager.GetProfileById(id);
            if (profile == null)
                return NotFound("App Profile not found");

            return Ok(profile);
        }

        [HttpGet("name/{profileName}")]
        public IActionResult GetAppProfileByName(string profileName)
        {
            var profile = _profileManager.GetProfileByName(profileName);
            if (profile == null)
                return NotFound("App Profile not found");

            return Ok(profile);
        }

        [HttpPost]
        public IActionResult CreateAppProfile([FromBody] ClientProfile profile)
        {
            if (profile == null)
                return BadRequest("Invalid profile data");

            // Mã hóa thông tin đăng nhập nếu được cung cấp
            if (!string.IsNullOrEmpty(profile.SteamUsername) && !profile.SteamUsername.StartsWith("w5"))
            {
                profile.SteamUsername = _profileManager.EncryptString(profile.SteamUsername);
            }

            if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.SteamPassword.StartsWith("HEQ"))
            {
                profile.SteamPassword = _profileManager.EncryptString(profile.SteamPassword);
            }

            var result = _profileManager.AddProfile(profile);
            return CreatedAtAction(nameof(GetAppProfileById), new { id = result.Id }, result);
        }

        [HttpPut("{id:int}")]
        public IActionResult UpdateAppProfile(int id, [FromBody] ClientProfile profile)
        {
            if (profile == null || id != profile.Id)
                return BadRequest("Invalid profile data");

            var existingProfile = _profileManager.GetProfileById(id);
            if (existingProfile == null)
                return NotFound("App Profile not found");

            // Mã hóa thông tin đăng nhập nếu được cập nhật
            if (!string.IsNullOrEmpty(profile.SteamUsername) && !profile.SteamUsername.StartsWith("w5"))
            {
                profile.SteamUsername = _profileManager.EncryptString(profile.SteamUsername);
            }

            if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.SteamPassword.StartsWith("HEQ"))
            {
                profile.SteamPassword = _profileManager.EncryptString(profile.SteamPassword);
            }

            bool updated = _profileManager.UpdateProfile(profile);
            if (!updated)
                return NotFound("Failed to update App Profile");

            return Ok(profile);
        }

        [HttpDelete("{id:int}")]
        public IActionResult DeleteAppProfile(int id)
        {
            bool deleted = _profileManager.DeleteProfile(id);
            if (!deleted)
                return NotFound("App Profile not found");

            return NoContent();
        }

        [HttpGet("{id:int}/decrypt")]
        public IActionResult GetDecryptedCredentials(int id)
        {
            var profile = _profileManager.GetProfileById(id);
            if (profile == null)
                return NotFound("App Profile not found");

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
    }
}