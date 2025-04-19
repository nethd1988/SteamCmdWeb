using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using SteamCmdWebAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SyncController : ControllerBase
    {
        private readonly SyncService _syncService;
        private readonly DecryptionService _decryptionService;
        private readonly ILogger<SyncController> _logger;

        public SyncController(
            SyncService syncService,
            DecryptionService decryptionService,
            ILogger<SyncController> logger)
        {
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("pending")]
        public IActionResult GetPendingProfiles()
        {
            try
            {
                var pendingProfiles = _syncService.GetPendingProfiles();
                return Ok(new
                {
                    success = true,
                    count = pendingProfiles.Count,
                    profiles = pendingProfiles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách profile đang chờ");
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("profiles")]
        public IActionResult SyncProfiles([FromBody] List<SteamCmdWebAPI.Models.SteamCmdProfile> clientProfiles)
        {
            if (clientProfiles == null || !clientProfiles.Any())
            {
                return BadRequest(new { success = false, message = "Không có profiles để đồng bộ" });
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            _logger.LogInformation("Nhận yêu cầu đồng bộ từ {ClientIp} với {Count} profiles", clientIp, clientProfiles.Count);

            try
            {
                int pendingCount = 0;

                foreach (var clientProfile in clientProfiles)
                {
                    // Chuyển đổi sang ClientProfile và thêm vào danh sách chờ
                    var pendingProfile = new ClientProfile
                    {
                        Name = clientProfile.Name,
                        AppID = clientProfile.AppID,
                        InstallDirectory = clientProfile.InstallDirectory,
                        SteamUsername = clientProfile.SteamUsername,
                        SteamPassword = clientProfile.SteamPassword,
                        Arguments = clientProfile.Arguments,
                        ValidateFiles = clientProfile.ValidateFiles,
                        AutoRun = clientProfile.AutoRun,
                        AnonymousLogin = clientProfile.AnonymousLogin,
                        Status = "Ready",
                        StartTime = DateTime.Now,
                        StopTime = DateTime.Now,
                        LastRun = DateTime.UtcNow
                    };

                    _syncService.AddPendingProfile(pendingProfile);
                    pendingCount++;
                }

                _logger.LogInformation("Đồng bộ hoàn tất. Đã thêm: {Added} vào danh sách chờ", pendingCount);

                return Ok(new
                {
                    success = true,
                    message = $"Đồng bộ hoàn tất. Đã thêm: {pendingCount} vào danh sách chờ",
                    pending = pendingCount,
                    total = clientProfiles.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ profiles từ {ClientIp}", clientIp);
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("profile")]
        public IActionResult SyncProfile([FromBody] SteamCmdWebAPI.Models.SteamCmdProfile clientProfile)
        {
            if (clientProfile == null)
            {
                return BadRequest(new { success = false, message = "Không có profile để đồng bộ" });
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            _logger.LogInformation("Nhận yêu cầu đồng bộ profile từ {ClientIp}: {ProfileName}", clientIp, clientProfile.Name);

            try
            {
                // Log chi tiết thông tin nhận được
                _logger.LogInformation("Thông tin profile nhận được: Name={Name}, AppID={AppID}, Username={Username}, Password={Password}, Anonymous={Anonymous}",
                    clientProfile.Name, clientProfile.AppID,
                    clientProfile.SteamUsername, clientProfile.SteamPassword,
                    clientProfile.AnonymousLogin);

                // Chuyển đổi sang ClientProfile
                var pendingProfile = new ClientProfile
                {
                    Name = clientProfile.Name ?? "Unnamed Profile",
                    AppID = clientProfile.AppID ?? "",
                    InstallDirectory = clientProfile.InstallDirectory ?? "",
                    SteamUsername = clientProfile.SteamUsername ?? "",
                    SteamPassword = clientProfile.SteamPassword ?? "",
                    Arguments = clientProfile.Arguments ?? "",
                    ValidateFiles = clientProfile.ValidateFiles,
                    AutoRun = clientProfile.AutoRun,
                    AnonymousLogin = clientProfile.AnonymousLogin,
                    Status = "Ready",
                    StartTime = DateTime.Now,
                    StopTime = DateTime.Now,
                    LastRun = DateTime.UtcNow
                };

                _syncService.AddPendingProfile(pendingProfile);

                _logger.LogInformation("Đã thêm profile {ProfileName} từ client vào danh sách chờ với thông tin đăng nhập: {Username}/{Password}",
                    pendingProfile.Name, pendingProfile.SteamUsername, pendingProfile.SteamPassword);

                return Ok(new
                {
                    success = true,
                    message = $"Đã thêm profile {clientProfile.Name} vào danh sách chờ",
                    pending = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ profile từ {ClientIp}", clientIp);
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }
    }
}