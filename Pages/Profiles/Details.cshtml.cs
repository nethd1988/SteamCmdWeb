using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Threading.Tasks;

namespace SteamCmdWeb.Pages.Profiles
{
    public class DetailsModel : PageModel
    {
        private readonly ProfileService _profileService;
        private readonly ILogger<DetailsModel> _logger;

        public ClientProfile Profile { get; set; }

        public DetailsModel(
            ProfileService profileService,
            ILogger<DetailsModel> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            try
            {
                Profile = await _profileService.GetProfileByIdAsync(id);
                if (Profile == null)
                {
                    _logger.LogWarning("Không tìm thấy profile có ID {0}", id);
                    return Page();
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải profile có ID {0}", id);
                return Page();
            }
        }
    }
}