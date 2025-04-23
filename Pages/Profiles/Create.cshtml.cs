using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Threading.Tasks;

namespace SteamCmdWeb.Pages.Profiles
{
    public class CreateModel : PageModel
    {
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;
        private readonly ILogger<CreateModel> _logger;

        [BindProperty]
        public ClientProfile Profile { get; set; } = new ClientProfile();

        [TempData]
        public string SuccessMessage { get; set; }

        public string ErrorMessage { get; set; }

        public CreateModel(
            ProfileService profileService,
            DecryptionService decryptionService,
            ILogger<CreateModel> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void OnGet()
        {
            // Khởi tạo profile mới với giá trị mặc định
            Profile = new ClientProfile
            {
                Status = "Ready",
                StartTime = DateTime.Now,
                StopTime = DateTime.Now,
                LastRun = DateTime.UtcNow
            };
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

                // Kiểm tra thông tin đăng nhập bắt buộc
                if (string.IsNullOrEmpty(Profile.SteamUsername) || string.IsNullOrEmpty(Profile.SteamPassword))
                {
                    ErrorMessage = "Thông tin đăng nhập là bắt buộc";
                    return Page();
                }

                // Mã hóa thông tin đăng nhập
                Profile.SteamUsername = _decryptionService.EncryptString(Profile.SteamUsername);
                Profile.SteamPassword = _decryptionService.EncryptString(Profile.SteamPassword);

                // Đặt trạng thái mặc định
                Profile.Status = "Ready";
                Profile.StartTime = DateTime.Now;
                Profile.StopTime = DateTime.Now;
                Profile.LastRun = DateTime.UtcNow;
                Profile.Pid = 0;

                // Tạo profile mới
                var newProfile = await _profileService.AddProfileAsync(Profile);
                _logger.LogInformation("Đã tạo profile mới: {0} (ID: {1})", newProfile.Name, newProfile.Id);

                SuccessMessage = $"Đã tạo profile '{newProfile.Name}' thành công.";
                return RedirectToPage("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo profile mới");
                ErrorMessage = $"Đã xảy ra lỗi khi tạo profile: {ex.Message}";
                return Page();
            }
        }
    }
}