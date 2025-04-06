using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProfileSyncController : ControllerBase
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<ProfileSyncController> _logger;
        private readonly SilentSyncService _silentSyncService;

        public ProfileSyncController(
            AppProfileManager profileManager,
            ILogger<ProfileSyncController> logger,
            SilentSyncService silentSyncService)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _silentSyncService = silentSyncService ?? throw new ArgumentNullException(nameof(silentSyncService));
        }

        [HttpPost("receive")]
        public async Task<IActionResult> ReceiveProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    _logger.LogWarning("Received null profile data");
                    return BadRequest("Invalid profile data");
                }

                _logger.LogInformation("Received profile: {Name} (ID: {Id})", profile.Name, profile.Id);
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                (bool success, string message) = await _silentSyncService.ReceiveProfileAsync(profile, clientIp);

                if (success)
                {
                    return Ok(new { Success = true, Message = "Profile added successfully", ProfileId = profile.Id });
                }
                return StatusCode(500, new { Success = false, Message = message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing profile");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost("batch")]
        public async Task<IActionResult> ReceiveProfiles([FromBody] List<ClientProfile> profiles)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Received empty or null profile batch");
                    return BadRequest("No profiles provided");
                }
                if (profiles.Count > 100)
                {
                    return BadRequest("Too many profiles in one request (max: 100)");
                }

                _logger.LogInformation("Received batch of {Count} profiles", profiles.Count);
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                (bool success, string message, int addedCount, int updatedCount, int errorCount, List<int> processedIds) =
                    await _silentSyncService.ReceiveProfilesAsync(profiles, clientIp);

                if (success)
                {
                    return Ok(new
                    {
                        Success = true,
                        Message = message,
                        ProfileIds = processedIds,
                        Added = addedCount,
                        Updated = updatedCount,
                        Errors = errorCount
                    });
                }
                return StatusCode(500, new
                {
                    Success = false,
                    Message = message,
                    Added = addedCount,
                    Updated = updatedCount,
                    Errors = errorCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing profile batch");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost("sync")]
        public async Task<IActionResult> SyncProfiles([FromBody] List<ClientProfile> profiles, [FromQuery] string clientId = null)
        {
            try
            {
                if (profiles == null)
                {
                    _logger.LogWarning("Received null profiles for sync");
                    return BadRequest("Invalid profiles data");
                }

                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                clientId = clientId ?? clientIp ?? "unknown";

                _logger.LogInformation("Starting profile sync from client {ClientId}. Profile count: {Count}", clientId, profiles.Count);
                string jsonProfiles = JsonSerializer.Serialize(profiles);

                (bool success, string message, int totalCount, int addedCount, int updatedCount, int errorCount) =
                    await _silentSyncService.ProcessFullSyncAsync(jsonProfiles, clientIp);

                if (success)
                {
                    return Ok(new
                    {
                        Success = true,
                        SyncId = Guid.NewGuid().ToString().Substring(0, 8),
                        Message = message,
                        Details = new { Added = addedCount, Updated = updatedCount, Errors = errorCount, Total = totalCount, Timestamp = DateTime.Now }
                    });
                }
                return StatusCode(500, new
                {
                    Success = false,
                    Message = message,
                    Details = new { Added = addedCount, Updated = updatedCount, Errors = errorCount, Total = totalCount }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during profile sync");
                return StatusCode(500, new { Success = false, Message = $"Sync failed: {ex.Message}" });
            }
        }
    }
}