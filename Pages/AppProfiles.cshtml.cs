using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamCmdWeb.Services;

namespace SteamCmdWeb.Pages
{
    public class AppProfilesModel : PageModel
    {
        private readonly AppProfileManager _profileManager;

        public AppProfilesModel(AppProfileManager profileManager)
        {
            _profileManager = profileManager;
        }

        public void OnGet()
        {
            // Không cần tải dữ liệu ở đây vì sẽ dùng JavaScript để tải từ API
        }
    }
}