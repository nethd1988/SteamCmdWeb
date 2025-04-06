using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SilentSyncController : ControllerBase
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<SilentSyncController> _logger;
        private readonly SilentSyncService _silentSyncService;

        public SilentSyncController(
            AppProfileManager profileManager,
            ILogger<SilentSyncController> logger,
            SilentSyncService silentSyncService)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _silentSyncService = silentSyncService ?? throw new ArgumentNullException(nameof(silentSyncService));
        }

        [HttpPost("profile")]
        public async Task<IActionResult> ReceiveProfile([FromBody] ClientProfile profile)
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Nhận request SilentSync/Profile từ IP: {ClientIp}", clientIp);

                if (profile == null)
                {
                    _logger.LogWarning("Nhận null profile từ IP: {ClientIp}", clientIp);
                    return BadRequest(new { Success = false, Message = "Dữ liệu profile không hợp lệ" });
                }

                (bool success, string message) = await _silentSyncService.ReceiveProfileAsync(profile, clientIp);

                if (success)
                {
                    return Ok(new { Success = true, Message = message });
                }
                else
                {
                    return StatusCode(500, new { Success = false, Message = message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xử lý profile từ IP: {ClientIp}", clientIp);
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("batch")]
        public async Task<IActionResult> ReceiveProfileBatch([FromBody] List<ClientProfile> profiles)
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Nhận request SilentSync/Batch từ IP: {ClientIp}, count: {Count}", clientIp, profiles?.Count ?? 0);

                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Nhận batch profiles trống từ IP: {ClientIp}", clientIp);
                    return BadRequest(new { Success = false, Message = "Không có profiles để xử lý" });
                }
                if (profiles.Count > 100)
                {
                    return BadRequest("Too many profiles in one request (max: 100)");
                }

                (bool success, string message, int added, int updated, int errors, List<int> processedIds) =
                    await _silentSyncService.ReceiveProfilesAsync(profiles, clientIp);

                if (success)
                {
                    return Ok(new
                    {
                        Success = true,
                        Message = message,
                        Added = added,
                        Updated = updated,
                        Errors = errors,
                        ProcessedIds = processedIds
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        Success = false,
                        Message = message,
                        Added = added,
                        Updated = updated,
                        Errors = errors
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xử lý batch profiles từ IP: {ClientIp}", clientIp);
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("full")]
        public async Task<IActionResult> FullSync()
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Nhận request SilentSync/Full từ IP: {ClientIp}", clientIp);

                using var reader = new StreamReader(Request.Body);
                string requestBody = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogWarning("Nhận body trống từ IP: {ClientIp}", clientIp);
                    return BadRequest(new { Success = false, Message = "Body rỗng" });
                }

                (bool success, string message, int totalProfiles, int added, int updated, int errors) =
                    await _silentSyncService.ProcessFullSyncAsync(requestBody, clientIp);

                if (success)
                {
                    return Ok(new
                    {
                        Success = true,
                        Message = message,
                        TotalProfiles = totalProfiles,
                        Added = added,
                        Updated = updated,
                        Errors = errors,
                        Timestamp = DateTime.Now
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        Success = false,
                        Message = message,
                        TotalProfiles = totalProfiles,
                        Added = added,
                        Updated = updated,
                        Errors = errors
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xử lý full sync từ IP: {ClientIp}", clientIp);
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpGet("status")]
        public IActionResult GetSyncStatus()
        {
            try
            {
                _logger.LogInformation("Nhận request lấy status sync");

                var status = _silentSyncService.GetSyncStatus();

                if (status is Dictionary<string, object> statusDict)
                {
                    foreach (var key in statusDict.Keys.ToList())
                    {
                        if (statusDict[key] is DateTime dateTime && dateTime == DateTime.MinValue)
                        {
                            statusDict[key] = null;
                        }
                    }
                }

                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thông tin sync");
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpGet("info")]
        public IActionResult GetSyncServerInfo()
        {
            try
            {
                _logger.LogInformation("Nhận request lấy thông tin server sync");

                var serverInfo = new
                {
                    ServerVersion = "1.0.0",
                    ApiVersion = "1.0",
                    SupportedMethods = new[] { "profile", "batch", "full" },
                    MaxBatchSize = 100,
                    MaxRequestSize = 50 * 1024 * 1024, // 50MB
                    ServerTime = DateTime.Now,
                    ServerTimeUtc = DateTime.UtcNow,
                    RequiresAuthentication = true,
                    Features = new
                    {
                        BatchProcessing = true,
                        SilentSync = true,
                        HttpSync = true,
                        TcpSync = true,
                        Encryption = true,
                        DataCompression = false
                    }
                };

                return Ok(new { Success = true, Info = serverInfo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thông tin server sync");
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }
    }
}