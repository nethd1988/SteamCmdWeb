using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;

namespace SteamCmdWeb.Pages
{
    // Note: Đã loại bỏ yêu cầu đăng nhập để truy cập trang này
    public class AppProfilesModel : PageModel
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<AppProfilesModel> _logger;

        public List<ClientProfile> Profiles { get; set; }
        public ClientProfile CurrentProfile { get; set; }

        public AppProfilesModel(AppProfileManager profileManager, ILogger<AppProfilesModel> logger)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void OnGet()
        {
            try
            {
                Profiles = _profileManager.GetAllProfiles();
                _logger.LogInformation("Đã tải {Count} profiles", Profiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải danh sách profiles");
                Profiles = new List<ClientProfile>();
            }
        }

        public IActionResult OnPostAdd(string name, string appId, string installDirectory, string steamUsername, string steamPassword, bool anonymousLogin)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(installDirectory))
            {
                _logger.LogWarning("Dữ liệu profile không hợp lệ");
                TempData["ErrorMessage"] = "Vui lòng điền đầy đủ thông tin bắt buộc!";
                return RedirectToPage("./AppProfiles");
            }

            try
            {
                var profile = new ClientProfile
                {
                    Name = name,
                    AppID = appId,
                    InstallDirectory = installDirectory,
                    SteamUsername = steamUsername,
                    SteamPassword = string.IsNullOrEmpty(steamPassword) ? "" : _profileManager.EncryptString(steamPassword),
                    AnonymousLogin = anonymousLogin,
                    Status = "Ready",
                    StartTime = DateTime.Now,
                    StopTime = DateTime.Now,
                    LastRun = DateTime.UtcNow
                };

                _profileManager.AddProfile(profile);
                
                _logger.LogInformation("Đã thêm profile mới: {Name} (ID: {Id})", profile.Name, profile.Id);
                TempData["SuccessMessage"] = "Thêm profile thành công!";
                
                return RedirectToPage("./AppProfiles");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm profile mới");
                TempData["ErrorMessage"] = "Đã xảy ra lỗi khi thêm profile!";
                return RedirectToPage("./AppProfiles");
            }
        }
    }
}