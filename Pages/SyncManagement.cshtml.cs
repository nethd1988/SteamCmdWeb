using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;

namespace SteamCmdWeb.Pages
{
    public class SyncManagementModel : PageModel
    {
        private readonly ILogger<SyncManagementModel> _logger;
        private readonly SyncService _syncService;
        private readonly DecryptionService _decryptionService;

        [TempData]
        public string StatusMessage { get; set; }

        [TempData]
        public bool IsSuccess { get; set; }

        public List<ClientProfile> PendingProfiles { get; set; } = new List<ClientProfile>();

        public SyncManagementModel(
            ILogger<SyncManagementModel> logger,
            SyncService syncService,
            DecryptionService decryptionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
        }

        public void OnGet()
        {
            try
            {
                // Lấy danh sách profile đang chờ
                PendingProfiles = _syncService.GetPendingProfiles();

                // Giải mã thông tin đăng nhập cho tất cả các profile
                // Chú ý: Thông tin đã được giải mã lưu trực tiếp vào đối tượng profile
                // để đảm bảo luôn hiển thị ngay cả khi làm mới trang
                foreach (var profile in PendingProfiles)
                {
                    if (!profile.AnonymousLogin)
                    {
                        // Chỉ giải mã nếu chưa được giải mã (kiểm tra xem có phải chuỗi base64 không)
                        if (!string.IsNullOrEmpty(profile.SteamUsername))
                        {
                            try
                            {
                                if (IsBase64String(profile.SteamUsername))
                                {
                                    profile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Không thể giải mã tên đăng nhập cho profile {ProfileName}", profile.Name);
                            }
                        }

                        if (!string.IsNullOrEmpty(profile.SteamPassword))
                        {
                            try
                            {
                                if (IsBase64String(profile.SteamPassword))
                                {
                                    profile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Không thể giải mã mật khẩu cho profile {ProfileName}", profile.Name);
                            }
                        }
                    }
                }

                _logger.LogInformation("Đã tải trang quản lý profile đang chờ với {PendingCount} profile", PendingProfiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải trang quản lý profile đang chờ");
                StatusMessage = "Đã xảy ra lỗi khi tải trang: " + ex.Message;
                IsSuccess = false;
            }
        }

        // Hàm kiểm tra chuỗi có phải base64 không
        private bool IsBase64String(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim();
            return (s.Length % 4 == 0) && System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-zA-Z0-9\+/]*={0,3}$", System.Text.RegularExpressions.RegexOptions.None);
        }

        public async Task<IActionResult> OnPostConfirmAsync(int index)
        {
            try
            {
                bool success = await _syncService.ConfirmProfileAsync(index);
                if (success)
                {
                    StatusMessage = "Đã xác nhận và thêm profile vào hệ thống";
                    IsSuccess = true;
                }
                else
                {
                    StatusMessage = "Không tìm thấy profile đã chọn trong danh sách chờ";
                    IsSuccess = false;
                }
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xác nhận profile");
                StatusMessage = "Đã xảy ra lỗi khi xác nhận profile: " + ex.Message;
                IsSuccess = false;
                return RedirectToPage();
            }
        }

        public IActionResult OnPostRejectAsync(int index)
        {
            try
            {
                bool success = _syncService.RejectProfile(index);
                if (success)
                {
                    StatusMessage = "Đã từ chối profile";
                    IsSuccess = true;
                }
                else
                {
                    StatusMessage = "Không tìm thấy profile đã chọn trong danh sách chờ";
                    IsSuccess = false;
                }
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi từ chối profile");
                StatusMessage = "Đã xảy ra lỗi khi từ chối profile: " + ex.Message;
                IsSuccess = false;
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostConfirmAllAsync()
        {
            try
            {
                int count = await _syncService.ConfirmAllPendingProfilesAsync();
                StatusMessage = $"Đã xác nhận và thêm {count} profile vào hệ thống";
                IsSuccess = true;
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xác nhận tất cả profile");
                StatusMessage = "Đã xảy ra lỗi khi xác nhận tất cả profile: " + ex.Message;
                IsSuccess = false;
                return RedirectToPage();
            }
        }

        public IActionResult OnPostRejectAllAsync()
        {
            try
            {
                int count = _syncService.RejectAllPendingProfiles();
                StatusMessage = $"Đã từ chối {count} profile";
                IsSuccess = true;
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi từ chối tất cả profile");
                StatusMessage = "Đã xảy ra lỗi khi từ chối tất cả profile: " + ex.Message;
                IsSuccess = false;
                return RedirectToPage();
            }
        }
    }
}