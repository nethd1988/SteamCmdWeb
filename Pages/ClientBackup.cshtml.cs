using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamCmdWeb.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Pages
{
    public class ClientBackupModel : PageModel
    {
        private readonly ILogger<ClientBackupModel> _logger;

        public ClientBackupModel(ILogger<ClientBackupModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
            try
            {
                // Đảm bảo thư mục backup tồn tại
                string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backup");
                
                if (!Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                    _logger.LogInformation("Created backup directory at {Path}", backupFolder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing backup directories");
            }
        }
    }
}