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

        // Lấy danh sách client đã đăng ký
        [HttpGet("clients")]
        public IActionResult GetRegisteredClients()
        {
            try
            {
                var clients = _syncService.GetRegisteredClients();
                return Ok(clients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách client đã đăng ký");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Đăng ký client mới
        // Đảm bảo rằng tham số trong phương thức RegisterClient đã được cập nhật đúng kiểu
        [HttpPost("register")]
        public IActionResult RegisterClient([FromBody] Models.ClientRegistration client)
        {
            try
            {
                if (client == null)
                {
                    return BadRequest("Dữ liệu không hợp lệ");
                }

                // Thiết lập thông tin bổ sung
                client.RegisteredAt = DateTime.Now;

                // Nếu không cung cấp token, tạo một token đơn giản
                if (string.IsNullOrEmpty(client.AuthToken))
                {
                    client.AuthToken = Guid.NewGuid().ToString("N");
                }

                // Đăng ký với service
                _syncService.RegisterClient(client);

                return Ok(new
                {
                    success = true,
                    message = "Đăng ký thành công",
                    clientId = client.ClientId,
                    authToken = client.AuthToken
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đăng ký client");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Xóa đăng ký client
        [HttpDelete("unregister/{clientId}")]
        public IActionResult UnregisterClient(string clientId)
        {
            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    return BadRequest("ClientId không được để trống");
                }

                bool success = _syncService.UnregisterClient(clientId);

                if (success)
                {
                    return Ok(new { success = true, message = "Đã xóa đăng ký thành công" });
                }
                else
                {
                    return NotFound(new { success = false, message = "Không tìm thấy client với ID này" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa đăng ký client {ClientId}", clientId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Chạy đồng bộ thủ công từ một client
        [HttpPost("sync/{clientId}")]
        public async Task<IActionResult> SyncFromClient(string clientId)
        {
            try
            {
                var clients = _syncService.GetRegisteredClients();
                var client = clients.FirstOrDefault(c => c.ClientId == clientId);

                if (client == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy client" });
                }

                var result = await _syncService.SyncProfilesFromClientAsync(client);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId}", clientId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Chạy đồng bộ thủ công từ tất cả client
        [HttpPost("sync-all")]
        public async Task<IActionResult> SyncFromAllClients()
        {
            try
            {
                var results = await _syncService.SyncFromAllClientsAsync();
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
}