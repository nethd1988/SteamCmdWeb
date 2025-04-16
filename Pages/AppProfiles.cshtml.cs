using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;

namespace SteamCmdWeb.Pages
{
    [Authorize]
    public class AppProfilesModel : PageModel
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<AppProfilesModel> _logger;

        public List<ClientProfile> Profiles { get; set; }
        public ClientProfile CurrentProfile { get; set; }
        public bool ShowAddForm { get; set; }
        public bool ShowEditForm { get; set; }

        public AppProfilesModel(AppProfileManager profileManager, ILogger<AppProfilesModel> logger)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IActionResult OnGet(int? edit)
        {
            try
            {
                ShowAddForm = Request.Query["add"] == "true";
                ShowEditForm = edit.HasValue;

                if (ShowEditForm)
                {
                    CurrentProfile = _profileManager.GetProfileById(edit.Value);
                    if (CurrentProfile == null)
                    {
                        _logger.LogWarning("Không tìm thấy profile có ID {Id}", edit.Value);
                        TempData["ErrorMessage"] = "Không tìm thấy profile để chỉnh sửa!";
                        return RedirectToPage("./AppProfiles");
                    }
                    _logger.LogInformation("Hiển thị form chỉnh sửa profile {Id}", edit.Value);
                }

                Profiles = _profileManager.GetAllProfiles();
                _logger.LogInformation("Đã tải {Count} profiles", Profiles.Count);
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải danh sách profiles");
                TempData["ErrorMessage"] = "Đã xảy ra lỗi khi tải trang!";
                Profiles = new List<ClientProfile>();
                return Page();
            }
        }

        public IActionResult OnPostAdd(string name, string appId, string installDirectory, string steamUsername, string arguments, bool anonymousLogin)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(installDirectory))
            {
                _logger.LogWarning("Dữ liệu profile không hợp lệ");
                TempData["ErrorMessage"] = "Vui lòng điền đầy đủ thông tin bắt buộc!";
                return RedirectToPage("./AppProfiles", new { add = true });
            }

            try
            {
                var profile = new ClientProfile
                {
                    Id = 0,
                    Name = name,
                    AppID = appId,
                    InstallDirectory = installDirectory,
                    SteamUsername = steamUsername,
                    SteamPassword = "", // Không lưu mật khẩu
                    Arguments = arguments,
                    AnonymousLogin = anonymousLogin,
                    Status = "Ready",
                    StartTime = DateTime.Now,
                    StopTime = DateTime.Now,
                    LastRun = DateTime.UtcNow,
                    Pid = 0,
                    ValidateFiles = false,
                    AutoRun = false
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
                return RedirectToPage("./AppProfiles", new { add = true });
            }
        }

        public IActionResult OnPostEdit(int id, string name, string appId, string installDirectory, string steamUsername, string arguments, bool anonymousLogin)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(installDirectory))
            {
                _logger.LogWarning("Dữ liệu profile không hợp lệ");
                TempData["ErrorMessage"] = "Vui lòng điền đầy đủ thông tin bắt buộc!";
                return RedirectToPage("./AppProfiles", new { edit = id });
            }

            try
            {
                var existingProfile = _profileManager.GetProfileById(id);
                if (existingProfile == null)
                {
                    _logger.LogWarning("Không tìm thấy profile có ID {Id}", id);
                    TempData["ErrorMessage"] = "Không tìm thấy profile để chỉnh sửa!";
                    return RedirectToPage("./AppProfiles");
                }

                var updatedProfile = new ClientProfile
                {
                    Id = id,
                    Name = name,
                    AppID = appId,
                    InstallDirectory = installDirectory,
                    SteamUsername = steamUsername,
                    SteamPassword = existingProfile.SteamPassword, // Giữ lại mật khẩu cũ
                    Arguments = arguments,
                    AnonymousLogin = anonymousLogin,
                    Status = existingProfile.Status,
                    StartTime = existingProfile.StartTime,
                    StopTime = existingProfile.StopTime,
                    LastRun = existingProfile.LastRun,
                    Pid = existingProfile.Pid,
                    ValidateFiles = existingProfile.ValidateFiles,
                    AutoRun = existingProfile.AutoRun
                };

                bool updated = _profileManager.UpdateProfile(updatedProfile);
                if (!updated)
                {
                    _logger.LogWarning("Không thể cập nhật profile {Id}", id);
                    TempData["ErrorMessage"] = "Không thể cập nhật profile!";
                    return RedirectToPage("./AppProfiles", new { edit = id });
                }

                _logger.LogInformation("Đã cập nhật profile: {Name} (ID: {Id})", updatedProfile.Name, updatedProfile.Id);
                TempData["SuccessMessage"] = "Cập nhật profile thành công!";
                return RedirectToPage("./AppProfiles");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật profile {Id}", id);
                TempData["ErrorMessage"] = "Đã xảy ra lỗi khi cập nhật profile!";
                return RedirectToPage("./AppProfiles", new { edit = id });
            }
        }

        public IActionResult OnPostDelete(int id)
        {
            try
            {
                var profile = _profileManager.GetProfileById(id);
                if (profile == null)
                {
                    _logger.LogWarning("Không tìm thấy profile có ID {Id} để xóa", id);
                    TempData["ErrorMessage"] = "Không tìm thấy profile để xóa!";
                    return RedirectToPage("./AppProfiles");
                }

                bool deleted = _profileManager.DeleteProfile(id);
                if (!deleted)
                {
                    _logger.LogWarning("Không thể xóa profile {Id}", id);
                    TempData["ErrorMessage"] = "Không thể xóa profile!";
                    return RedirectToPage("./AppProfiles");
                }

                _logger.LogInformation("Đã xóa profile: {Name} (ID: {Id})", profile.Name, id);
                TempData["SuccessMessage"] = "Xóa profile thành công!";
                return RedirectToPage("./AppProfiles");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile {Id}", id);
                TempData["ErrorMessage"] = "Đã xảy ra lỗi khi xóa profile!";
                return RedirectToPage("./AppProfiles");
            }
        }
    }
}