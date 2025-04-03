using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System.Collections.Generic;

namespace SteamCmdWeb.Pages
{
    public class ProfilesModel : PageModel
    {
        private readonly AppProfileManager _profileManager;

        public ProfilesModel(AppProfileManager profileManager)
        {
            _profileManager = profileManager;
        }

        public void OnGet()
        {
            // Không cần lấy dữ liệu ở đây vì chúng ta sẽ sử dụng JavaScript để tải dữ liệu từ API
        }
    }
}