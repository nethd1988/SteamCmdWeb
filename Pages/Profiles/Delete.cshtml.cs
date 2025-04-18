using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;
using System;
using System.Threading.Tasks;

namespace SteamCmdWeb.Pages.Profiles
{
    public class DeleteModel : PageModel
    {
        private readonly ProfileService _profileService;
        private readonly ILogger<DeleteModel> _logger;

        [TempData]
        public string SuccessMessage { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public DeleteModel(
            ProfileService profileService,
            ILogger<DeleteModel> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IActionResult> OnPostAsync(int id)
        {
            try
            {
                var profile = await _profileService.GetProfileByIdAsync(id);
                if (profile == null)
                {
                    ErrorMessage = $"Không tìm thấy profile có ID {id}.";
                    return RedirectToPage("Index");
                }

                var result = await _profileService.DeleteProfileAsync(id);
                if (result)
                {
                    _logger.LogInformation("Đã xóa profile có ID {0}", id);
                    SuccessMessage = $"Đã xóa profile '{profile.Name}' thành công.";
                }
                else
                {
                    _logger.LogWarning("Không thể xóa profile có ID {0}", id);
                    ErrorMessage = $"Không thể xóa profile '{profile.Name}'.";
                }

                return RedirectToPage("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xóa profile có ID {0}", id);
                ErrorMessage = $"Đã xảy ra lỗi khi xóa profile: {ex.Message}";
                return RedirectToPage("Index");
            }
        }
    }
}