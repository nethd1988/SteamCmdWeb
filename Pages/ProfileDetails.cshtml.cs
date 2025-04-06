using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;

namespace SteamCmdWeb.Pages
{
    public class ProfileDetailsModel : PageModel
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<ProfileDetailsModel> _logger;

        public ClientProfile Profile { get; set; }

        public ProfileDetailsModel(AppProfileManager profileManager, ILogger<ProfileDetailsModel> logger)
        {
            _profileManager = profileManager;
            _logger = logger;
        }

        public IActionResult OnGet(int id)
        {
            _logger.LogInformation("Trang ProfileDetails được yêu cầu cho ID: {Id}", id);
            
            // Lấy thông tin profile
            Profile = _profileManager.GetProfileById(id);
            
            if (Profile == null)
            {
                _logger.LogWarning("Không tìm thấy profile với ID: {Id}", id);
                return Page(); // Trang sẽ hiển thị thông báo lỗi
            }
            
            _logger.LogInformation("Đã tìm thấy profile: {Id} - {Name}", Profile.Id, Profile.Name);
            return Page();
        }
    }
}