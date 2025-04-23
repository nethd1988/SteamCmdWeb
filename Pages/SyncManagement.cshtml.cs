// File: Pages/SyncManagement.cshtml.cs
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
                foreach (var profile in PendingProfiles)
                {
                    try
                    {
                        // Không cần kiểm tra IsBased64String vì có thể gây lỗi với một số ký tự đặc biệt
                        if (!string.IsNullOrEmpty(profile.SteamUsername))
                        {
                            try
                            {
                                string decryptedUsername = _decryptionService.DecryptString(profile.SteamUsername);
                                if (!string.IsNullOrEmpty(decryptedUsername))
                                {
                                    profile.SteamUsername = decryptedUsername;
                                }
                                // Nếu giải mã trả về chuỗi rỗng, giữ nguyên giá trị
                            }
                            catch
                            {
                                // Giữ nguyên giá trị nếu không thể giải mã
                            }
                        }

                        // Mật khẩu hiển thị dưới dạng đã được mã hóa
                        if (!string.IsNullOrEmpty(profile.SteamPassword))
                        {
                            try
                            {
                                string decryptedPassword = _decryptionService.DecryptString(profile.SteamPassword);
                                if (!string.IsNullOrEmpty(decryptedPassword))
                                {
                                    profile.SteamPassword = decryptedPassword;
                                }
                            }
                            catch
                            {
                                // Giữ nguyên giá trị nếu không thể giải mã
                            }
                        }
                    }
                    catch
                    {
                        // Nếu có lỗi, giữ nguyên giá trị
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải trang quản lý profile đang chờ");
                StatusMessage = "Đã xảy ra lỗi khi tải trang: " + ex.Message;
                IsSuccess = false;
            }
        }

        // Kiểm tra chuỗi có phải base64 không
        private bool IsBase64String(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim();
            return (s.Length % 4 == 0) && System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-zA-Z0-9\+/]*={0,3}$", System.Text.RegularExpressions.RegexOptions.None);
        }

        // Kiểm tra chuỗi có thể đọc được không (tránh hiển thị các ký tự không đọc được)
        private bool IsReadableString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;

            // Kiểm tra xem có ít nhất 70% ký tự có thể đọc được
            int readableChars = 0;
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsWhiteSpace(c))
                {
                    readableChars++;
                }
            }

            return ((double)readableChars / s.Length) >= 0.7;
        }

        // Các phương thức xử lý POST không thay đổi
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