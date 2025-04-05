using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamCmdWeb.Services;

namespace SteamCmdWeb.Pages
{
    public class ClientBackupModel : PageModel
    {
        private readonly ProfileMigrationService _profileMigrationService;

        public ClientBackupModel(ProfileMigrationService profileMigrationService)
        {
            _profileMigrationService = profileMigrationService;
        }

        public void OnGet()
        {
            // No need to load data here as we'll use JavaScript to fetch data from the API
        }
    }
}