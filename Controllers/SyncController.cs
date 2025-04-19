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
        private readonly DecryptionService _decryptionService;
        private readonly ILogger<SyncController> _logger;

        public SyncController(
            ProfileService profileService,
            DecryptionService decryptionService,
            ILogger<SyncController> logger)
        {
            _profileService = profileService;
            _decryptionService = decryptionService;
            _logger = logger;
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

                int added = 0;
                int skipped = 0;

                foreach (var clientProfile in clientProfiles)
                {
                    // Chỉ lấy các profile có AppID chưa tồn tại
                    if (!existingAppIds.Contains(clientProfile.AppID))
                    {
                        // Chuyển đổi từ SteamCmdProfile sang ClientProfile
                        var newProfile = new ClientProfile
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

                        await _profileService.AddProfileAsync(newProfile);
                        existingAppIds.Add(clientProfile.AppID); // Cập nhật để không thêm trùng
                        added++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                _logger.LogInformation("Đồng bộ hoàn tất. Đã thêm: {Added}, Bỏ qua: {Skipped}", added, skipped);

                return Ok(new
                {
                    success = true,
                    message = $"Đồng bộ hoàn tất. Đã thêm: {added}, Bỏ qua: {skipped}",
                    added,
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
                if (!existingAppIds.Contains(clientProfile.AppID))
                {
                    // Chuyển đổi từ SteamCmdProfile sang ClientProfile
                    var newProfile = new ClientProfile
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

                    await _profileService.AddProfileAsync(newProfile);
                    _logger.LogInformation("Đã thêm profile mới từ client: {ProfileName}, AppID: {AppID}",
                        clientProfile.Name, clientProfile.AppID);

                    return Ok(new
                    {
                        success = true,
                        message = $"Đã thêm profile {clientProfile.Name}",
                        added = true
                    });
                }
                else
                {
                    _logger.LogInformation("Bỏ qua profile từ client vì AppID đã tồn tại: {AppID}", clientProfile.AppID);
                    return Ok(new
                    {
                        success = true,
                        message = $"Bỏ qua profile {clientProfile.Name} vì AppID {clientProfile.AppID} đã tồn tại",
                        added = false
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ profile từ {ClientIp}", clientIp);
                return StatusCode(500, new { success = false, message = $"Lỗi: {ex.Message}" });
            }
        }
    }
}