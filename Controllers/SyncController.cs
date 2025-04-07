using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SteamCmdWeb.Controllers
{
    [Route("api/sync")]
    [ApiController]
    [Authorize]
    public class SyncController : ControllerBase
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<SyncController> _logger;
        private readonly SilentSyncService _silentSyncService;
        private readonly string _dataFolder;

        public SyncController(
            AppProfileManager profileManager,
            ILogger<SyncController> logger,
            SilentSyncService silentSyncService)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _silentSyncService = silentSyncService ?? throw new ArgumentNullException(nameof(silentSyncService));
            _dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        }

        /// <summary>
        /// Nhận một profile từ client
        /// </summary>
        [HttpPost("profile")]
        public async Task<IActionResult> ReceiveProfile([FromBody] ClientProfile profile)
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Nhận request Sync/Profile từ IP: {ClientIp}", clientIp);

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

        /// <summary>
        /// Nhận một batch profiles từ client
        /// </summary>
        [HttpPost("batch")]
        public async Task<IActionResult> ReceiveProfileBatch([FromBody] List<ClientProfile> profiles)
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Nhận request Sync/Batch từ IP: {ClientIp}, count: {Count}", 
                    clientIp, profiles?.Count ?? 0);

                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogWarning("Nhận batch profiles trống từ IP: {ClientIp}", clientIp);
                    return BadRequest(new { Success = false, Message = "Không có profiles để xử lý" });
                }
                
                if (profiles.Count > 100)
                {
                    return BadRequest(new { Success = false, Message = "Quá nhiều profiles trong một request (tối đa: 100)" });
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

        /// <summary>
        /// Xử lý đồng bộ đầy đủ
        /// </summary>
        [HttpPost("full")]
        public async Task<IActionResult> FullSync()
        {
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                _logger.LogInformation("Nhận request Sync/Full từ IP: {ClientIp}", clientIp);

                string requestBody;
                using (var reader = new StreamReader(Request.Body))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

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

        /// <summary>
        /// Lấy thông tin trạng thái đồng bộ
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

        /// <summary>
        /// Lấy thông tin server đồng bộ
        /// </summary>
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
                    SupportedEndpoints = new[] { 
                        "/api/sync/profile", 
                        "/api/sync/batch", 
                        "/api/sync/full",
                        "/api/sync/status",
                        "/api/sync/info"
                    },
                    SupportedMethods = new[] { "POST", "GET" },
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
        
        /// <summary>
        /// Đồng bộ profiles từ client
        /// </summary>
        [HttpPost("client")]
        public async Task<IActionResult> SyncProfiles([FromBody] List<ClientProfile> profiles, [FromQuery] string clientId = null)
        {
            try
            {
                if (profiles == null)
                {
                    _logger.LogWarning("Received null profiles for sync");
                    return BadRequest(new { Success = false, Message = "Invalid profiles data" });
                }

                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                clientId = clientId ?? clientIp ?? "unknown";

                _logger.LogInformation("Starting profile sync from client {ClientId}. Profile count: {Count}", 
                    clientId, profiles.Count);
                
                string jsonProfiles = JsonSerializer.Serialize(profiles);

                (bool success, string message, int totalCount, int addedCount, int updatedCount, int errorCount) =
                    await _silentSyncService.ProcessFullSyncAsync(jsonProfiles, clientIp);

                if (success)
                {
                    // Backup trước khi xử lý
                    await BackupReceivedProfiles(profiles, clientIp);
                    
                    return Ok(new
                    {
                        Success = true,
                        SyncId = Guid.NewGuid().ToString().Substring(0, 8),
                        Message = message,
                        Details = new { 
                            Added = addedCount, 
                            Updated = updatedCount, 
                            Errors = errorCount, 
                            Total = totalCount, 
                            Timestamp = DateTime.Now 
                        }
                    });
                }
                
                return StatusCode(500, new
                {
                    Success = false,
                    Message = message,
                    Details = new { 
                        Added = addedCount, 
                        Updated = updatedCount, 
                        Errors = errorCount, 
                        Total = totalCount 
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
        /// Tạo bản sao lưu cho profiles đã nhận từ client
        /// </summary>
        private async Task BackupReceivedProfiles(List<ClientProfile> profiles, string clientIp)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    return;
                }

                string backupFolder = Path.Combine(_dataFolder, "Backup", "ClientSync");
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"sync_{clientIp.Replace(':', '_')}_{timestamp}.json";
                string filePath = Path.Combine(backupFolder, fileName);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonContent = JsonSerializer.Serialize(profiles, options);
                await System.IO.File.WriteAllTextAsync(filePath, jsonContent);

                _logger.LogInformation("BackupReceivedProfiles: Đã sao lưu {Count} profiles từ {ClientIp} vào file {FileName}", 
                    profiles.Count, clientIp, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi sao lưu profiles từ client {ClientIp}", clientIp);
                // Không throw exception để không ảnh hưởng đến luồng xử lý chính
            }
        }
    }
}