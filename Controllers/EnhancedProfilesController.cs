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
        /// Get all profiles with optional decryption
        /// </summary>
        [HttpGet]
        public IActionResult GetProfiles([FromQuery] bool decrypt = false)
        {
            try
            {
                var profiles = _profileManager.GetAllProfiles();
                
                if (decrypt)
                {
                    // Add decrypted credentials to result (only for viewing, not stored in profiles)
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
                _logger.LogError(ex, "Error getting profiles with decryption");
                return StatusCode(500, new { Error = "Failed to retrieve profiles", Message = ex.Message });
            }
        }
        
        /// <summary>
        /// Get a profile by ID with optional decryption
        /// </summary>
        [HttpGet("{id:int}")]
        public IActionResult GetProfileById(int id, [FromQuery] bool decrypt = false)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    return NotFound(new { Error = "Profile not found" });
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
                _logger.LogError(ex, "Error getting profile {ProfileId} with decryption", id);
                return StatusCode(500, new { Error = "Failed to retrieve profile", Message = ex.Message });
            }
        }
        
        /// <summary>
        /// Get decrypted credentials for a profile
        /// </summary>
        [HttpGet("{id:int}/credentials")]
        public IActionResult GetDecryptedCredentials(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    return NotFound(new { Error = "Profile not found" });
                }
                
                if (profile.AnonymousLogin)
                {
                    return Ok(new { Message = "This profile uses anonymous login (no credentials)" });
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
                _logger.LogError(ex, "Error getting credentials for profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Failed to decrypt credentials", Message = ex.Message });
            }
        }
        
        /// <summary>
        /// Create a new profile with proper encryption
        /// </summary>
        [HttpPost]
        public IActionResult CreateProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    return BadRequest(new { Error = "Invalid profile data" });
                }
                
                // Validate required fields
                if (string.IsNullOrEmpty(profile.Name) || string.IsNullOrEmpty(profile.AppID))
                {
                    return BadRequest(new { Error = "Name and AppID are required" });
                }
                
                // Encrypt credentials if not anonymous login
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
                
                // Set default values
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
                _logger.LogError(ex, "Error creating profile");
                return StatusCode(500, new { Error = "Failed to create profile", Message = ex.Message });
            }
        }
        
        /// <summary>
        /// Update a profile with proper encryption
        /// </summary>
        [HttpPut("{id:int}")]
        public IActionResult UpdateProfile(int id, [FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null || id != profile.Id)
                {
                    return BadRequest(new { Error = "Invalid profile data or ID mismatch" });
                }
                
                var existingProfile = _profileManager.GetProfileById(id);
                if (existingProfile == null)
                {
                    return NotFound(new { Error = "Profile not found" });
                }
                
                // Encrypt credentials if not anonymous login and credentials were updated
                if (!profile.AnonymousLogin)
                {
                    // Only encrypt if the value appears to be plain text (not already encrypted)
                    if (!string.IsNullOrEmpty(profile.SteamUsername) && !profile.SteamUsername.Contains("/") && !profile.SteamUsername.Contains("="))
                    {
                        profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                    }
                    else if (string.IsNullOrEmpty(profile.SteamUsername))
                    {
                        // Keep existing username if not provided
                        profile.SteamUsername = existingProfile.SteamUsername;
                    }
                    
                    if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.SteamPassword.Contains("/") && !profile.SteamPassword.Contains("="))
                    {
                        profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                    }
                    else if (string.IsNullOrEmpty(profile.SteamPassword))
                    {
                        // Keep existing password if not provided
                        profile.SteamPassword = existingProfile.SteamPassword;
                    }
                }
                
                bool updated = _profileManager.UpdateProfile(profile);
                if (!updated)
                {
                    return NotFound(new { Error = "Failed to update profile" });
                }
                
                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Failed to update profile", Message = ex.Message });
            }
        }
        
        /// <summary>
        /// Delete a profile
        /// </summary>
        [HttpDelete("{id:int}")]
        public IActionResult DeleteProfile(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    return NotFound(new { Error = "Profile not found" });
                }
                
                bool deleted = _profileManager.DeleteProfile(id);
                if (!deleted)
                {
                    return NotFound(new { Error = "Failed to delete profile" });
                }
                
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Failed to delete profile", Message = ex.Message });
            }
        }
        
        /// <summary>
        /// Duplicate a profile
        /// </summary>
        [HttpPost("{id:int}/duplicate")]
        public IActionResult DuplicateProfile(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    return NotFound(new { Error = "Profile not found" });
                }
                
                // Create a copy of the profile
                var copy = new ClientProfile
                {
                    Id = 0, // Will be assigned by the manager
                    Name = $"{profile.Name} (Copy)",
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
                _logger.LogError(ex, "Error duplicating profile {ProfileId}", id);
                return StatusCode(500, new { Error = "Failed to duplicate profile", Message = ex.Message });
            }
        }
    }
}