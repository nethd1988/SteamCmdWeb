using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;
using System;
using System.Threading.Tasks;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SteamAppsController : ControllerBase
    {
        private readonly SteamAppService _steamAppService;
        private readonly ILogger<SteamAppsController> _logger;

        public SteamAppsController(
            SteamAppService steamAppService,
            ILogger<SteamAppsController> logger)
        {
            _steamAppService = steamAppService ?? throw new ArgumentNullException(nameof(steamAppService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("name/{appId}")]
        public async Task<IActionResult> GetAppName(string appId)
        {
            try
            {
                var appName = await _steamAppService.GetAppNameAsync(appId);
                return Ok(new { appId, name = appName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy tên game cho AppID {AppId}", appId);
                return StatusCode(500, new { error = "Lỗi khi lấy tên game" });
            }
        }
    }
} 