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
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void OnGet()
        {
            _logger.LogInformation("Trang Profiles được yêu cầu");
            // Dữ liệu sẽ được tải thông qua JavaScript từ API '/api/appprofiles'.
            // Không cần lấy dữ liệu trực tiếp trong phương thức này.
        }
    }
}