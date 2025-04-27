using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Services;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClientTrackingController : ControllerBase
    {
        private readonly ClientTrackingService _clientTrackingService;
        private readonly ILogger<ClientTrackingController> _logger;

        public ClientTrackingController(
            ClientTrackingService clientTrackingService,
            ILogger<ClientTrackingController> logger)
        {
            _clientTrackingService = clientTrackingService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult GetAllClients()
        {
            try
            {
                var clients = _clientTrackingService.GetAllClients();
                return Ok(new
                {
                    success = true,
                    clients = clients,
                    onlineCount = _clientTrackingService.GetOnlineClientCount(),
                    totalCount = _clientTrackingService.GetTotalClientCount()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client list");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("{clientId}")]
        public IActionResult GetClientInfo(string clientId)
        {
            try
            {
                var clientInfo = _clientTrackingService.GetClientInfo(clientId);
                if (clientInfo == null)
                {
                    return NotFound(new { success = false, message = "Client not found" });
                }

                return Ok(new { success = true, client = clientInfo });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting client info for {ClientId}", clientId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("track")]
        public IActionResult TrackClient([FromBody] TrackClientRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ClientId))
                {
                    return BadRequest(new { success = false, message = "ClientId is required" });
                }

                _clientTrackingService.TrackClient(request.ClientId, request.RemoteIp, request.InverterIp);
                return Ok(new { success = true, message = "Client tracked successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking client");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("update-status")]
        public IActionResult UpdateClientStatus([FromBody] UpdateStatusRequest request)
        {
            try
            {
                _clientTrackingService.UpdateClientStatus(request.ClientId, request.Status);
                return Ok(new { success = true, message = "Status updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating client status");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpDelete("{clientId}")]
        public IActionResult RemoveClient(string clientId)
        {
            try
            {
                _clientTrackingService.RemoveClient(clientId);
                return Ok(new { success = true, message = "Client removed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing client {ClientId}", clientId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    public class TrackClientRequest
    {
        public string ClientId { get; set; }
        public string RemoteIp { get; set; }
        public string InverterIp { get; set; }
    }

    public class UpdateStatusRequest
    {
        public string ClientId { get; set; }
        public string Status { get; set; }
    }
}