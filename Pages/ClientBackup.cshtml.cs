using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamCmdWeb.Services;
using System.IO;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Pages
{
    public class ClientBackupModel : PageModel
    {
        private readonly ProfileMigrationService _profileMigrationService;
        private readonly ILogger<ClientBackupModel> _logger;

        public ClientBackupModel(ProfileMigrationService profileMigrationService, ILogger<ClientBackupModel> logger)
        {
            _profileMigrationService = profileMigrationService;
            _logger = logger;
        }

        public void OnGet()
        {
            // Check backup folder exists
            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backup");
            if (!Directory.Exists(backupFolder))
            {
                try
                {
                    Directory.CreateDirectory(backupFolder);
                    _logger.LogInformation("Created backup directory at {Path}", backupFolder);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating backup directory");
                }
            }
        }
    }
}