using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
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

                // Không gửi mật khẩu đã mã hóa cho client
                var sanitizedProfiles = pendingProfiles.Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.AppID,
                    p.InstallDirectory,
                    p.Arguments,
                    p.ValidateFiles,
                    p.AutoRun,
                    SteamUsername = "***",
                    SteamPassword = "***",
                    p.Status
                }).ToList();

                return Ok(new
                {
                    success = true,
                    count = pendingProfiles.Count,
                    profiles = sanitizedProfiles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách profile đang chờ");
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("profiles")]
        public IActionResult SyncProfiles([FromBody] List<dynamic> clientProfiles)
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
                    try
                    {
                        // Trích xuất thông tin từ dynamic object
                        string name = clientProfile.Name?.ToString() ?? "Unnamed Profile";
                        string appId = clientProfile.AppID?.ToString() ?? "";
                        string installDir = clientProfile.InstallDirectory?.ToString() ?? "";
                        string steamUsername = clientProfile.SteamUsername?.ToString() ?? "";
                        string steamPassword = clientProfile.SteamPassword?.ToString() ?? "";
                        string arguments = clientProfile.Arguments?.ToString() ?? "";
                        bool validateFiles = clientProfile.ValidateFiles != null && Convert.ToBoolean(clientProfile.ValidateFiles);
                        bool autoRun = clientProfile.AutoRun != null && Convert.ToBoolean(clientProfile.AutoRun);

                        // Kiểm tra thông tin đăng nhập
                        if (string.IsNullOrEmpty(steamUsername) || string.IsNullOrEmpty(steamPassword))
                        {
                            _logger.LogWarning("Từ chối profile {Name} vì thiếu thông tin đăng nhập", name);
                            continue;
                        }

                        // Chuyển đổi sang ClientProfile và thêm vào danh sách chờ
                        var pendingProfile = new ClientProfile
                        {
                            Name = name,
                            AppID = appId,
                            InstallDirectory = installDir,
                            SteamUsername = steamUsername,
                            SteamPassword = steamPassword,
                            Arguments = arguments,
                            ValidateFiles = validateFiles,
                            AutoRun = autoRun,
                            Status = "Ready",
                            StartTime = DateTime.Now,
                            StopTime = DateTime.Now,
                            LastRun = DateTime.UtcNow
                        };

                        _syncService.AddPendingProfile(pendingProfile);
                        pendingCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile từ client");
                    }
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
        public IActionResult SyncProfile([FromBody] dynamic clientProfile)
        {
            if (clientProfile == null)
            {
                return BadRequest(new { success = false, message = "Không có profile để đồng bộ" });
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            try
            {
                // Trích xuất thông tin từ dynamic object
                string name = clientProfile.Name?.ToString() ?? "Unnamed Profile";
                string appId = clientProfile.AppID?.ToString() ?? "";
                string installDir = clientProfile.InstallDirectory?.ToString() ?? "";
                string steamUsername = clientProfile.SteamUsername?.ToString() ?? "";
                string steamPassword = clientProfile.SteamPassword?.ToString() ?? "";
                string arguments = clientProfile.Arguments?.ToString() ?? "";
                bool validateFiles = clientProfile.ValidateFiles != null && Convert.ToBoolean(clientProfile.ValidateFiles);
                bool autoRun = clientProfile.AutoRun != null && Convert.ToBoolean(clientProfile.AutoRun);

                _logger.LogInformation("Nhận yêu cầu đồng bộ profile từ {ClientIp}: {ProfileName}", clientIp, name);

                // Kiểm tra thông tin đăng nhập
                if (string.IsNullOrEmpty(steamUsername) || string.IsNullOrEmpty(steamPassword))
                {
                    return BadRequest(new { success = false, message = "Thiếu thông tin đăng nhập" });
                }

                // Log chi tiết thông tin nhận được
                _logger.LogInformation("Thông tin profile nhận được: Name={Name}, AppID={AppID}, Username={Username}",
                    name, appId, steamUsername);

                // Chuyển đổi sang ClientProfile
                var pendingProfile = new ClientProfile
                {
                    Name = name,
                    AppID = appId,
                    InstallDirectory = installDir,
                    SteamUsername = steamUsername,
                    SteamPassword = steamPassword,
                    Arguments = arguments,
                    ValidateFiles = validateFiles,
                    AutoRun = autoRun,
                    Status = "Ready",
                    StartTime = DateTime.Now,
                    StopTime = DateTime.Now,
                    LastRun = DateTime.UtcNow
                };

                _syncService.AddPendingProfile(pendingProfile);

                _logger.LogInformation("Đã thêm profile {ProfileName} từ client vào danh sách chờ", name);

                return Ok(new
                {
                    success = true,
                    message = $"Đã thêm profile {name} vào danh sách chờ",
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