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
        private readonly ProfileService _profileService;
        private readonly SyncService _syncService;
        private readonly DecryptionService _decryptionService;
        private readonly ILogger<SyncController> _logger;

        public SyncController(
            ProfileService profileService,
            SyncService syncService,
            DecryptionService decryptionService,
            ILogger<SyncController> logger)
        {
            _profileService = profileService;
            _syncService = syncService;
            _decryptionService = decryptionService;
            _logger = logger;
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

        [HttpPost("confirm/{index}")]
        public async Task<IActionResult> ConfirmProfile(int index)
        {
            try
            {
                bool success = await _syncService.ConfirmProfileAsync(index);
                if (success)
                {
                    return Ok(new { success = true, message = "Đã xác nhận và thêm profile vào danh sách chính" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Không tìm thấy profile trong danh sách chờ" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xác nhận profile");
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("reject/{index}")]
        public IActionResult RejectProfile(int index)
        {
            try
            {
                bool success = _syncService.RejectProfile(index);
                if (success)
                {
                    return Ok(new { success = true, message = "Đã từ chối profile" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Không tìm thấy profile trong danh sách chờ" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi từ chối profile");
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("confirm-all")]
        public async Task<IActionResult> ConfirmAllProfiles()
        {
            try
            {
                int addedCount = await _syncService.ConfirmAllPendingProfilesAsync();
                return Ok(new
                {
                    success = true,
                    message = $"Đã xác nhận và thêm {addedCount} profile vào danh sách chính",
                    count = addedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xác nhận tất cả profile");
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("reject-all")]
        public IActionResult RejectAllProfiles()
        {
            try
            {
                int rejectedCount = _syncService.RejectAllPendingProfiles();
                return Ok(new
                {
                    success = true,
                    message = $"Đã từ chối {rejectedCount} profile",
                    count = rejectedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi từ chối tất cả profile");
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("profiles")]
        public async Task<IActionResult> SyncProfiles([FromBody] List<SteamCmdProfile> clientProfiles)
        {
            if (clientProfiles == null || !clientProfiles.Any())
            {
                return BadRequest(new { success = false, message = "Không có profiles để đồng bộ" });
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            _logger.LogInformation("Nhận yêu cầu đồng bộ từ {ClientIp} với {Count} profiles", clientIp, clientProfiles.Count);

            try
            {
                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                int pendingCount = 0;
                int skipped = 0;

                foreach (var clientProfile in clientProfiles)
                {
                    // Kiểm tra AppID đã tồn tại chưa
                    if (existingAppIds.Contains(clientProfile.AppID))
                    {
                        skipped++;
                        continue;
                    }

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

                    lock (_syncService)
                    {
                        _syncService.GetPendingProfiles().Add(pendingProfile);
                    }
                    pendingCount++;
                }

                _logger.LogInformation("Đồng bộ hoàn tất. Đã thêm: {Added} vào danh sách chờ, Bỏ qua: {Skipped}", pendingCount, skipped);

                return Ok(new
                {
                    success = true,
                    message = $"Đồng bộ hoàn tất. Đã thêm: {pendingCount} vào danh sách chờ, Bỏ qua: {skipped}",
                    pending = pendingCount,
                    skipped,
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
        public async Task<IActionResult> SyncProfile([FromBody] SteamCmdProfile clientProfile)
        {
            if (clientProfile == null)
            {
                return BadRequest(new { success = false, message = "Không có profile để đồng bộ" });
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            _logger.LogInformation("Nhận yêu cầu đồng bộ profile từ {ClientIp}: {ProfileName}", clientIp, clientProfile.Name);

            try
            {
                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                // Kiểm tra AppID đã tồn tại chưa
                if (existingAppIds.Contains(clientProfile.AppID))
                {
                    _logger.LogInformation("Bỏ qua profile từ client vì AppID đã tồn tại: {AppID}", clientProfile.AppID);
                    return Ok(new
                    {
                        success = true,
                        message = $"Bỏ qua profile {clientProfile.Name} vì AppID {clientProfile.AppID} đã tồn tại",
                        added = false
                    });
                }

                // Chuyển đổi sang ClientProfile
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

                lock (_syncService)
                {
                    _syncService.GetPendingProfiles().Add(pendingProfile);
                }

                _logger.LogInformation("Đã thêm profile {ProfileName} từ client vào danh sách chờ", clientProfile.Name);

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