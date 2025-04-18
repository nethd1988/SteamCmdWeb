using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SteamCmdWeb.Pages.Profiles
{
    public class IndexModel : PageModel
    {
        private readonly ProfileService _profileService;
        private readonly ILogger<IndexModel> _logger;

        public List<ClientProfile> Profiles { get; private set; } = new List<ClientProfile>();

        [TempData]
        public string SuccessMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public IndexModel(ProfileService profileService, ILogger<IndexModel> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task OnGetAsync()
        {
            try
            {
                Profiles = await _profileService.GetAllProfilesAsync();
                _logger.LogInformation("Đã tải {0} profiles", Profiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải danh sách profiles");
                ErrorMessage = "Đã xảy ra lỗi khi tải danh sách profiles.";
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            try
            {
                var profile = await _profileService.GetProfileByIdAsync(id);
                if (profile == null)
                {
                    ErrorMessage = $"Không tìm thấy profile có ID {id}.";
                    return RedirectToPage();
                }

                var success = await _profileService.DeleteProfileAsync(id);
                if (success)
                {
                    SuccessMessage = $"Đã xóa profile '{profile.Name}' thành công.";
                }
                else
                {
                    ErrorMessage = $"Không thể xóa profile '{profile.Name}'.";
                }

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile có ID {0}", id);
                ErrorMessage = "Đã xảy ra lỗi khi xóa profile.";
                return RedirectToPage();
            }
        }
    }
}