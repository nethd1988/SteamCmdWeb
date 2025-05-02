using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Threading.Tasks;

namespace SteamCmdWeb.Pages.Profiles
{
    public class EditModel : PageModel
    {
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;
        private readonly ILogger<EditModel> _logger;

        [BindProperty]
        public ClientProfile Profile { get; set; }

        [BindProperty]
        public string NewUsername { get; set; }

        [BindProperty]
        public string NewPassword { get; set; }

        [TempData]
        public string SuccessMessage { get; set; }

        public string ErrorMessage { get; set; }

        public EditModel(
            ProfileService profileService,
            DecryptionService decryptionService,
            ILogger<EditModel> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            try
            {
                Profile = await _profileService.GetProfileByIdAsync(id);
                if (Profile == null)
                {
                    return NotFound();
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải profile có ID {0}", id);
                ErrorMessage = $"Đã xảy ra lỗi khi tải profile: {ex.Message}";
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = string.Join("; ", ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));
                    ErrorMessage = $"Thông tin không hợp lệ: {errors}";
                    return Page();
                }

                // Chỉ cập nhật 2 trường: Name và AppID
                var existingProfile = await _profileService.GetProfileByIdAsync(Profile.Id);
                if (existingProfile == null)
                {
                    ErrorMessage = "Không tìm thấy profile để cập nhật.";
                    return Page();
                }
                existingProfile.Name = Profile.Name;
                existingProfile.AppID = Profile.AppID;

                await _profileService.UpdateProfileAsync(existingProfile);
                SuccessMessage = $"Đã cập nhật profile '{existingProfile.Name}' thành công.";
                return RedirectToPage("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật profile");
                ErrorMessage = $"Đã xảy ra lỗi khi cập nhật profile: {ex.Message}";
                return Page();
            }
        }
    }
}