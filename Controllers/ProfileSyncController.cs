using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProfileSyncController : ControllerBase
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<ProfileSyncController> _logger;

        public ProfileSyncController(AppProfileManager profileManager, ILogger<ProfileSyncController> logger)
        {
            _profileManager = profileManager;
            _logger = logger;
        }

        /// <summary>
        /// Nhận profile từ client và lưu vào hệ thống
        /// </summary>
        [HttpPost("receive")]
        public IActionResult ReceiveProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    _logger.LogWarning("Received null profile data");
                    return BadRequest("Invalid profile data");
                }

                _logger.LogInformation("Received profile: {Name} (ID: {Id})", profile.Name, profile.Id);

                // Kiểm tra xem profile đã tồn tại chưa
                var existingProfile = _profileManager.GetProfileById(profile.Id);
                
                if (existingProfile == null)
                {
                    // Thêm mới profile
                    var result = _profileManager.AddProfile(profile);
                    _logger.LogInformation("Added new profile: {Name} (ID: {Id})", result.Name, result.Id);
                    return Ok(new { Success = true, Message = "Profile added successfully", ProfileId = result.Id });
                }
                else
                {
                    // Cập nhật profile hiện có
                    bool updated = _profileManager.UpdateProfile(profile);
                    if (updated)
                    {
                        _logger.LogInformation("Updated existing profile: {Name} (ID: {Id})", profile.Name, profile.Id);
                        return Ok(new { Success = true, Message = "Profile updated successfully", ProfileId = profile.Id });
                    }
                    else
                    {
                        _logger.LogWarning("Failed to update profile: {Name} (ID: {Id})", profile.Name, profile.Id);
                        return StatusCode(500, new { Success = false, Message = "Failed to update profile" });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing profile");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Nhận nhiều profile cùng lúc từ client
        /// </summary>
        [HttpPost("batch")]
        public IActionResult ReceiveProfiles([FromBody] List<ClientProfile> profiles)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Received empty or null profile batch");
                    return BadRequest("No profiles provided");
                }

                _logger.LogInformation("Received batch of {Count} profiles", profiles.Count);

                int addedCount = 0;
                int updatedCount = 0;
                List<int> processedIds = new List<int>();

                foreach (var profile in profiles)
                {
                    if (profile == null) continue;

                    var existingProfile = _profileManager.GetProfileById(profile.Id);
                    
                    if (existingProfile == null)
                    {
                        // Thêm mới profile
                        var result = _profileManager.AddProfile(profile);
                        processedIds.Add(result.Id);
                        addedCount++;
                    }
                    else
                    {
                        // Cập nhật profile hiện có
                        bool updated = _profileManager.UpdateProfile(profile);
                        if (updated)
                        {
                            processedIds.Add(profile.Id);
                            updatedCount++;
                        }
                    }
                }

                _logger.LogInformation("Batch processing completed: Added {AddedCount}, Updated {UpdatedCount}", 
                    addedCount, updatedCount);

                return Ok(new { 
                    Success = true, 
                    Message = $"Processed {addedCount + updatedCount} profiles (Added: {addedCount}, Updated: {updatedCount})",
                    ProfileIds = processedIds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing profile batch");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Đồng bộ hóa toàn bộ profile từ client đến server
        /// </summary>
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

                _logger.LogInformation("Starting profile sync from client {ClientId}. Profile count: {Count}", 
                    clientId ?? "unknown", profiles.Count);

                // Lưu trữ dữ liệu đồng bộ để phân tích
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string syncId = Guid.NewGuid().ToString().Substring(0, 8);
                
                // Xử lý từng profile
                int addedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;
                
                foreach (var profile in profiles)
                {
                    try
                    {
                        if (profile == null) continue;
                        
                        var existingProfile = _profileManager.GetProfileById(profile.Id);
                        
                        if (existingProfile == null)
                        {
                            // Thêm mới profile
                            _profileManager.AddProfile(profile);
                            addedCount++;
                        }
                        else
                        {
                            // Cập nhật profile hiện có
                            bool updated = _profileManager.UpdateProfile(profile);
                            if (updated)
                            {
                                updatedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing profile {Id} during sync", profile?.Id);
                        errorCount++;
                    }
                }

                _logger.LogInformation("Sync completed: Added {AddedCount}, Updated {UpdatedCount}, Errors {ErrorCount}", 
                    addedCount, updatedCount, errorCount);

                return Ok(new { 
                    Success = true, 
                    SyncId = syncId,
                    Message = $"Sync completed successfully. Added: {addedCount}, Updated: {updatedCount}, Errors: {errorCount}",
                    Details = new {
                        Added = addedCount,
                        Updated = updatedCount,
                        Errors = errorCount,
                        Timestamp = DateTime.Now
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during profile sync");
                return StatusCode(500, new { Success = false, Message = $"Sync failed: {ex.Message}" });
            }
        }

        /// <summary>
        /// Nhận thông tin về các profile từ một server từ xa
        /// </summary>
        [HttpGet("sync")]
        public async Task<IActionResult> SyncFromRemoteServer(
            [FromQuery] string targetServer, 
            [FromQuery] int port = 61188,
            [FromQuery] bool force = false)
        {
            try
            {
                if (string.IsNullOrEmpty(targetServer))
                {
                    return BadRequest("Target server address is required");
                }

                _logger.LogInformation("Initiating sync from remote server: {Server}:{Port}", targetServer, port);

                // TODO: Implement the actual TCP connection to the remote server
                // This would use similar code to what's in your TcpClientService
                
                // For now, we'll return a placeholder response
                return Ok(new {
                    Success = true,
                    Message = $"Sync from {targetServer}:{port} requested. Implementation in progress.",
                    Details = new {
                        TargetServer = targetServer,
                        Port = port,
                        Force = force,
                        Status = "Pending"
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing from remote server {Server}:{Port}", targetServer, port);
                return StatusCode(500, new { Success = false, Message = $"Sync failed: {ex.Message}" });
            }
        }
    }
}