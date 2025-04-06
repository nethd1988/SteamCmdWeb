using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System.Collections.Generic;

namespace SteamCmdWeb.Pages
{
    public class ProfilesModel : PageModel
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<ProfilesModel> _logger;

        public List<ClientProfile> Profiles { get; set; } = new List<ClientProfile>();

        public ProfilesModel(AppProfileManager profileManager, ILogger<ProfilesModel> logger)
        {
            _profileManager = profileManager;
            _logger = logger;
        }

        public void OnGet()
        {
            _logger.LogInformation("Trang Profiles được yêu cầu");
            // Không lấy dữ liệu ở đây vì chúng ta sẽ sử dụng JavaScript để tải dữ liệu từ API
        }
    }
}