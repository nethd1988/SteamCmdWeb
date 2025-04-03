using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System.IO;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProfilesController : ControllerBase
    {
        private readonly string _configFolder = Path.Combine(Directory.GetCurrentDirectory(), "Profiles");
        private readonly AppProfileManager _profileManager;

        public ProfilesController(AppProfileManager profileManager)
        {
            _profileManager = profileManager;
        }

        [HttpGet]
        public IActionResult GetProfiles()
        {
            var profiles = _profileManager.GetAllProfiles();
            return Ok(profiles);
        }

        [HttpGet("legacy")]
        public IActionResult GetLegacyProfiles()
        {
            string[] profiles = Directory.GetFiles(_configFolder, "*.profile");
            if (profiles.Length == 0)
                return Ok(new string[] { });
            return Ok(profiles.Select(Path.GetFileNameWithoutExtension));
        }

        [HttpGet("{id:int}")]
        public IActionResult GetProfileById(int id)
        {
            var profile = _profileManager.GetProfileById(id);
            if (profile == null)
                return NotFound("Profile not found");

            return Ok(profile);
        }

        [HttpGet("name/{profileName}")]
        public IActionResult GetProfileByName(string profileName)
        {
            var profile = _profileManager.GetProfileByName(profileName);
            if (profile == null)
                return NotFound("Profile not found");

            return Ok(profile);
        }

        [HttpGet("legacy/{profileName}")]
        public IActionResult GetLegacyProfileDetails(string profileName)
        {
            string filePath = Path.Combine(_configFolder, $"{profileName}.profile");
            if (!System.IO.File.Exists(filePath))
                return NotFound("Profile not found");

            GameProfile profile = GameProfile.LoadFromFile(filePath);
            return Ok(profile);
        }

        [HttpPost]
        public IActionResult CreateProfile([FromBody] ClientProfile profile)
        {
            if (profile == null)
                return BadRequest("Invalid profile data");

            var result = _profileManager.AddProfile(profile);
            return CreatedAtAction(nameof(GetProfileById), new { id = result.Id }, result);
        }

        [HttpPut("{id:int}")]
        public IActionResult UpdateProfile(int id, [FromBody] ClientProfile profile)
        {
            if (profile == null || id != profile.Id)
                return BadRequest("Invalid profile data");

            bool updated = _profileManager.UpdateProfile(profile);
            if (!updated)
                return NotFound("Profile not found");

            return Ok(profile);
        }

        [HttpDelete("{id:int}")]
        public IActionResult DeleteProfile(int id)
        {
            bool deleted = _profileManager.DeleteProfile(id);
            if (!deleted)
                return NotFound("Profile not found");

            return NoContent();
        }

        [HttpPost("convert")]
        public IActionResult ConvertLegacyToClientProfile([FromBody] GameProfile gameProfile)
        {
            if (gameProfile == null)
                return BadRequest("Invalid profile data");

            var clientProfile = _profileManager.ConvertToClientProfile(gameProfile);
            return Ok(clientProfile);
        }
    }
}