using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;
using SteamCmdWeb.Models;
using System.Linq;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SyncManagementController : ControllerBase
    {
        private readonly ILogger<SyncManagementController> _logger;
        private readonly SyncService _syncService;

        public SyncManagementController(
            ILogger<SyncManagementController> logger,
            SyncService syncService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        }

        // Lấy kết quả đồng bộ gần đây
        [HttpGet("results")]
        public IActionResult GetSyncResults()
        {
            try
            {
                var results = _syncService.GetSyncResults();
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách kết quả đồng bộ");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Đồng bộ từ một địa chỉ IP cụ thể
        [HttpPost("sync-ip")]
        public async Task<IActionResult> SyncFromIp([FromBody] SyncIpRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrEmpty(request.Ip))
                {
                    return BadRequest(new { success = false, message = "Địa chỉ IP không được để trống" });
                }

                int port = request.Port > 0 ? request.Port : 61188;
                var result = await _syncService.SyncFromIpAsync(request.Ip, port);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ địa chỉ IP {Ip}", request.Ip);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Quét mạng để tìm client
        [HttpPost("scan-network")]
        public async Task<IActionResult> ScanNetwork()
        {
            try
            {
                await _syncService.ScanLocalNetworkAsync();
                return Ok(new { success = true, message = "Đã quét mạng và đồng bộ với các client tìm thấy" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi quét mạng");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Đồng bộ từ tất cả các client đã biết
        [HttpPost("sync-all")]
        public async Task<IActionResult> SyncFromAllClients()
        {
            try
            {
                var results = await _syncService.SyncFromAllKnownClientsAsync();
                return Ok(new
                {
                    success = true,
                    results = results,
                    totalClients = results.Count,
                    successfulSyncs = results.Count(r => r.Success),
                    totalNewProfiles = results.Sum(r => r.NewProfilesAdded)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ tất cả client");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class SyncIpRequest
    {
        public string Ip { get; set; }
        public int Port { get; set; } = 61188;
    }
}