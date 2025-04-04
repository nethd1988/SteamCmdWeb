using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SilentSyncController : ControllerBase
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<SilentSyncController> _logger;
        private readonly string _syncFolder;

        public SilentSyncController(AppProfileManager profileManager, ILogger<SilentSyncController> logger)
        {
            _profileManager = profileManager;
            _logger = logger;

            _syncFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "SilentSync");
            if (!Directory.Exists(_syncFolder))
            {
                Directory.CreateDirectory(_syncFolder);
            }
        }

        /// <summary>
        /// API để nhận một profile từ client một cách âm thầm
        /// </summary>
        [HttpPost("profile")]
        public IActionResult ReceiveProfile([FromBody] ClientProfile profile)
        {
            try
            {
                if (profile == null)
                {
                    _logger.LogWarning("Received null profile in silent sync");
                    return BadRequest("Invalid profile data");
                }

                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _logger.LogInformation("Silent sync received profile: {Name} (ID: {Id}) from {ClientIp}",
                    profile.Name, profile.Id, clientIp);

                // Lưu backup
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"profile_{profile.Id}_{timestamp}.json";
                string filePath = Path.Combine(_syncFolder, fileName);

                string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(filePath, json);

                // Thêm hoặc cập nhật profile
                var existingProfile = _profileManager.GetProfileById(profile.Id);
                if (existingProfile == null)
                {
                    var result = _profileManager.AddProfile(profile);
                    return Ok(new { Success = true, Action = "Added", ProfileId = result.Id });
                }
                else
                {
                    bool updated = _profileManager.UpdateProfile(profile);
                    if (updated)
                    {
                        return Ok(new { Success = true, Action = "Updated", ProfileId = profile.Id });
                    }
                    else
                    {
                        return StatusCode(500, new { Success = false, Message = "Failed to update profile" });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in silent profile sync");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// API để nhận nhiều profile từ client một cách âm thầm
        /// </summary>
        [HttpPost("batch")]
        public IActionResult ReceiveProfileBatch([FromBody] List<ClientProfile> profiles)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Received empty or null profile batch in silent sync");
                    return BadRequest("No profiles provided");
                }

                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _logger.LogInformation("Silent sync received batch of {Count} profiles from {ClientIp}",
                    profiles.Count, clientIp);

                // Lưu backup của toàn bộ batch
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"batch_{clientIp}_{timestamp}.json";
                string filePath = Path.Combine(_syncFolder, fileName);

                string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(filePath, json);

                // Xử lý từng profile
                int addedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;
                List<int> processedIds = new List<int>();

                foreach (var profile in profiles)
                {
                    try
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
                            else
                            {
                                errorCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing profile {Id} in batch", profile?.Id);
                        errorCount++;
                    }
                }

                _logger.LogInformation("Silent sync batch processed: Added {AddedCount}, Updated {UpdatedCount}, Errors {ErrorCount}",
                    addedCount, updatedCount, errorCount);

                return Ok(new
                {
                    Success = true,
                    Message = $"Processed {addedCount + updatedCount} profiles (Added: {addedCount}, Updated: {updatedCount}, Errors: {errorCount})",
                    Added = addedCount,
                    Updated = updatedCount,
                    Errors = errorCount,
                    ProcessedIds = processedIds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in silent batch sync");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// API để nhận full sync từ client
        /// </summary>
        [HttpPost("full")]
        public async Task<IActionResult> FullSync()
        {
            try
            {
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _logger.LogInformation("Full silent sync requested from {ClientIp}", clientIp);

                // Đọc raw JSON từ body request
                using var reader = new StreamReader(Request.Body);
                string requestBody = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogWarning("Received empty request body in full sync from {ClientIp}", clientIp);
                    return BadRequest("Empty request body");
                }

                // Lưu backup của dữ liệu gốc
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"fullsync_{clientIp}_{timestamp}.json";
                string filePath = Path.Combine(_syncFolder, fileName);

                System.IO.File.WriteAllText(filePath, requestBody);

                // Phân tích dữ liệu
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                };

                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(requestBody, options);

                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("No valid profiles found in full sync data from {ClientIp}", clientIp);
                    return BadRequest("No valid profiles in request data");
                }

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
                            else
                            {
                                errorCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing profile {Id} in full sync", profile?.Id);
                        errorCount++;
                    }
                }

                _logger.LogInformation("Full silent sync completed. Added: {AddedCount}, Updated: {UpdatedCount}, Errors: {ErrorCount}",
                    addedCount, updatedCount, errorCount);

                return Ok(new
                {
                    Success = true,
                    Message = $"Full sync completed successfully. Added: {addedCount}, Updated: {updatedCount}, Errors: {errorCount}",
                    TotalProfiles = profiles.Count,
                    Added = addedCount,
                    Updated = updatedCount,
                    Errors = errorCount,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in full silent sync");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Lấy thông tin về tình trạng đồng bộ hóa
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetSyncStatus()
        {
            try
            {
                // Lấy thông tin về lần đồng bộ cuối
                var syncFiles = new DirectoryInfo(_syncFolder)
                    .GetFiles("*.json")
                    .OrderByDescending(f => f.CreationTime)
                    .Take(10)
                    .Select(f => new
                    {
                        FileName = f.Name,
                        Size = f.Length,
                        CreationTime = f.CreationTime
                    })
                    .ToList();

                // Đếm các loại sync trong 24h qua
                var last24Hours = DateTime.Now.AddHours(-24);
                var recentFiles = new DirectoryInfo(_syncFolder)
                    .GetFiles("*.json")
                    .Where(f => f.CreationTime >= last24Hours);

                int profileSyncs = recentFiles.Count(f => f.Name.StartsWith("profile_"));
                int batchSyncs = recentFiles.Count(f => f.Name.StartsWith("batch_"));
                int fullSyncs = recentFiles.Count(f => f.Name.StartsWith("fullsync_"));

                return Ok(new
                {
                    LastSyncFiles = syncFiles,
                    SyncStats = new
                    {
                        Last24Hours = new
                        {
                            ProfileSyncs = profileSyncs,
                            BatchSyncs = batchSyncs,
                            FullSyncs = fullSyncs,
                            TotalSyncs = profileSyncs + batchSyncs + fullSyncs
                        }
                    },
                    TotalProfiles = _profileManager.GetAllProfiles().Count,
                    Status = "Active",
                    CurrentTime = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync status");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }
    }
}