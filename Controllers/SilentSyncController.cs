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
        private readonly SilentSyncService _silentSyncService;

        public SilentSyncController(
            AppProfileManager profileManager,
            ILogger<SilentSyncController> logger,
            SilentSyncService silentSyncService)
        {
            _profileManager = profileManager;
            _logger = logger;
            _silentSyncService = silentSyncService;
        }

        /// <summary>
        /// API để nhận một profile từ client một cách âm thầm
        /// </summary>
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

                var (success, message) = await _silentSyncService.ReceiveProfileAsync(profile, clientIp);

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

        /// <summary>
        /// API để nhận nhiều profile từ client một cách âm thầm
        /// </summary>
        [HttpPost("batch")]
        public async Task<IActionResult> ReceiveProfileBatch([FromBody] List<ClientProfile> profiles)
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Nhận request SilentSync/Batch từ IP: {ClientIp}, count: {Count}",
                    clientIp, profiles?.Count ?? 0);

                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Nhận batch profiles trống từ IP: {ClientIp}", clientIp);
                    return BadRequest(new { Success = false, Message = "Không có profiles để xử lý" });
                }

                var (success, message, added, updated, errors, processedIds) =
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

        /// <summary>
        /// API để nhận full sync từ client
        /// </summary>
        [HttpPost("full")]
        public async Task<IActionResult> FullSync()
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Nhận request SilentSync/Full từ IP: {ClientIp}", clientIp);

                // Đọc raw JSON từ body request
                using var reader = new StreamReader(Request.Body);
                string requestBody = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogWarning("Nhận body trống từ IP: {ClientIp}", clientIp);
                    return BadRequest(new { Success = false, Message = "Body rỗng" });
                }

                var (success, message, totalProfiles, added, updated, errors) =
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

        /// <summary>
        /// Lấy thông tin về tình trạng đồng bộ hóa
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetSyncStatus()
        {
            try
            {
                _logger.LogInformation("Nhận request lấy status sync");

                var status = _silentSyncService.GetSyncStatus();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thông tin sync");
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }
    }
}