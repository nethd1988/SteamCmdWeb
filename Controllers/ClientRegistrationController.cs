using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;

namespace SteamCmdWeb.Controllers
{
    public class ClientRegistration
    {
        public string ClientId { get; set; }
        public string Description { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
        public string AuthToken { get; set; }
        public bool IsActive { get; set; }
        public DateTime RegisteredAt { get; set; }
        public DateTime LastSuccessfulSync { get; set; }
        public DateTime LastSyncAttempt { get; set; }
        public string LastSyncResults { get; set; }
        public int SyncIntervalMinutes { get; set; }
        public bool PushOnly { get; set; }
        public bool PullOnly { get; set; }
        public int ConnectionFailureCount { get; set; }
        public int SyncErrorCount { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class ClientRegistrationController : ControllerBase
    {
        private readonly ILogger<ClientRegistrationController> _logger;
        private static List<ClientRegistration> _clients = new List<ClientRegistration>();

        public ClientRegistrationController(ILogger<ClientRegistrationController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Lấy danh sách client đã đăng ký
        /// </summary>
        [HttpGet]
        public IActionResult GetRegisteredClients()
        {
            try
            {
                var clients = _clients;

                // Ẩn thông tin nhạy cảm (token) khi trả về
                var sanitizedClients = clients.Select(c => new
                {
                    c.ClientId,
                    c.Description,
                    c.Address,
                    c.Port,
                    c.IsActive,
                    c.RegisteredAt,
                    c.LastSuccessfulSync,
                    c.LastSyncAttempt,
                    c.LastSyncResults,
                    c.SyncIntervalMinutes,
                    c.PushOnly,
                    c.PullOnly,
                    c.ConnectionFailureCount,
                    c.SyncErrorCount,
                    Status = DetermineClientStatus(c)
                }).ToList();

                return Ok(sanitizedClients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting registered clients");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Đăng ký client mới
        /// </summary>
        [HttpPost("register")]
        public IActionResult RegisterClient([FromBody] ClientRegistration registration)
        {
            try
            {
                if (registration == null)
                {
                    return BadRequest("Invalid registration data");
                }

                // Xác thực thông tin
                if (string.IsNullOrEmpty(registration.ClientId) ||
                    string.IsNullOrEmpty(registration.Address))
                {
                    return BadRequest("ClientId and Address are required");
                }

                // Mặc định giá trị nếu không được cung cấp
                if (registration.Port <= 0) registration.Port = 61188;
                if (string.IsNullOrEmpty(registration.AuthToken)) registration.AuthToken = "simple_auth_token";
                if (registration.SyncIntervalMinutes <= 0) registration.SyncIntervalMinutes = 60;

                // Timestamp hiện tại
                registration.RegisteredAt = DateTime.UtcNow;

                // Thêm IP của client vào description nếu không có
                string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (string.IsNullOrEmpty(registration.Description))
                {
                    registration.Description = $"Registered from {clientIp} on {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }

                // Đăng ký client
                var existingClient = _clients.FirstOrDefault(c => c.ClientId == registration.ClientId);
                if (existingClient != null)
                {
                    // Cập nhật client hiện có
                    _clients.Remove(existingClient);
                }
                _clients.Add(registration);

                _logger.LogInformation("Successfully registered client {ClientId} at {Address}:{Port}",
                    registration.ClientId, registration.Address, registration.Port);

                return Ok(new
                {
                    Success = true,
                    Message = "Client registered successfully",
                    ClientId = registration.ClientId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering client");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Hủy đăng ký client
        /// </summary>
        [HttpPost("unregister/{clientId}")]
        public IActionResult UnregisterClient(string clientId)
        {
            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    return BadRequest("ClientId is required");
                }

                var existingClient = _clients.FirstOrDefault(c => c.ClientId == clientId);
                if (existingClient != null)
                {
                    _clients.Remove(existingClient);
                    _logger.LogInformation("Successfully unregistered client {ClientId}", clientId);
                    return Ok(new { Success = true, Message = "Client unregistered successfully" });
                }
                else
                {
                    return NotFound(new { Success = false, Message = "Client not found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unregistering client {ClientId}", clientId);
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Đặt trạng thái kích hoạt cho client
        /// </summary>
        [HttpPost("setactive/{clientId}")]
        public IActionResult SetClientActive(string clientId, [FromBody] bool isActive)
        {
            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    return BadRequest("ClientId is required");
                }

                var client = _clients.FirstOrDefault(c => c.ClientId == clientId);

                if (client == null)
                {
                    return NotFound(new { Success = false, Message = "Client not found" });
                }

                // Cập nhật trạng thái
                client.IsActive = isActive;

                _logger.LogInformation("Set client {ClientId} active status to {IsActive}", clientId, isActive);
                return Ok(new { Success = true, Message = $"Client active status set to {isActive}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting active status for client {ClientId}", clientId);
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Kiểm tra kết nối đến client
        /// </summary>
        [HttpGet("test/{clientId}")]
        public async Task<IActionResult> TestClientConnection(string clientId)
        {
            try
            {
                if (string.IsNullOrEmpty(clientId))
                {
                    return BadRequest("ClientId is required");
                }

                var client = _clients.FirstOrDefault(c => c.ClientId == clientId);

                if (client == null)
                {
                    return NotFound(new { Success = false, Message = "Client not found" });
                }

                // Kiểm tra kết nối
                bool pingSuccess = await PingHostAsync(client.Address);
                bool portSuccess = await CheckPortAsync(client.Address, client.Port);

                return Ok(new
                {
                    Success = true,
                    ClientId = client.ClientId,
                    Address = client.Address,
                    Port = client.Port,
                    PingResult = pingSuccess ? "Success" : "Failed",
                    PortResult = portSuccess ? "Open" : "Closed",
                    Status = pingSuccess && portSuccess ? "Reachable" : "Unreachable"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection to client {ClientId}", clientId);
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Xác định trạng thái hiện tại của client
        /// </summary>
        private string DetermineClientStatus(ClientRegistration client)
        {
            if (!client.IsActive) return "Inactive";

            if (client.ConnectionFailureCount > 5) return "Connection Issues";

            if (client.SyncErrorCount > 3) return "Sync Issues";

            if (client.LastSuccessfulSync == DateTime.MinValue) return "Pending First Sync";

            if ((DateTime.UtcNow - client.LastSuccessfulSync).TotalHours > 24) return "Sync Overdue";

            return "Active";
        }

        /// <summary>
        /// Ping đến host
        /// </summary>
        private async Task<bool> PingHostAsync(string hostNameOrAddress)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(hostNameOrAddress, 3000); // 3 second timeout
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Kiểm tra xem cổng có mở không
        /// </summary>
        private async Task<bool> CheckPortAsync(string host, int port)
        {
            try
            {
                using var client = new TcpClientAdapter();
                await client.ConnectAsync(host, port, TimeSpan.FromSeconds(5));
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Adapter cho TcpClient để hỗ trợ timeout
    /// </summary>
    public class TcpClientAdapter : IDisposable
    {
        private readonly System.Net.Sockets.TcpClient _tcpClient = new System.Net.Sockets.TcpClient();

        public bool Connected => _tcpClient.Connected;

        public async Task ConnectAsync(string host, int port, TimeSpan timeout)
        {
            using var cts = new System.Threading.CancellationTokenSource(timeout);

            try
            {
                var connectTask = _tcpClient.ConnectAsync(host, port);
                if (await Task.WhenAny(connectTask, Task.Delay(timeout)) != connectTask)
                {
                    throw new TimeoutException($"Connection to {host}:{port} timed out after {timeout.TotalSeconds}s");
                }

                await connectTask;
            }
            catch
            {
                // Đảm bảo giải phóng tài nguyên nếu kết nối thất bại
                _tcpClient.Close();
                throw;
            }
        }

        public void Dispose()
        {
            _tcpClient.Dispose();
        }
    }
}