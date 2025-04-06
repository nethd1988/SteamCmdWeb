using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System.Collections.Generic;

namespace SteamCmdWeb.Pages
{
    public class AppProfilesModel : PageModel
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<AppProfilesModel> _logger;

        public AppProfilesModel(AppProfileManager profileManager, ILogger<AppProfilesModel> logger)
        {
            _profileManager = profileManager;
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Trang AppProfiles được truy cập");
            // Không cần tải dữ liệu ở đây vì chúng ta sẽ sử dụng JavaScript để tải từ API
        }
    }
}