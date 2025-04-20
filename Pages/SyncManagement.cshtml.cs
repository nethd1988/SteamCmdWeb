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
                    if (!profile.AnonymousLogin)
                    {
                        try
                        {
                            // Kiểm tra và giải mã username
                            if (!string.IsNullOrEmpty(profile.SteamUsername) && IsBase64String(profile.SteamUsername))
                            {
                                profile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                            }

                            // Kiểm tra và giải mã password
                            if (!string.IsNullOrEmpty(profile.SteamPassword) && IsBase64String(profile.SteamPassword))
                            {
                                profile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Không thể giải mã thông tin đăng nhập cho profile {ProfileName}", profile.Name);
                            // Đặt giá trị mặc định nếu giải mã thất bại
                            if (string.IsNullOrEmpty(profile.SteamUsername) || !IsReadableString(profile.SteamUsername))
                            {
                                profile.SteamUsername = "(Không thể giải mã)";
                            }
                            if (string.IsNullOrEmpty(profile.SteamPassword) || !IsReadableString(profile.SteamPassword))
                            {
                                profile.SteamPassword = "(Không thể giải mã)";
                            }
                        }
                    }
                    else
                    {
                        // Đảm bảo hiển thị thông báo cho tài khoản ẩn danh
                        profile.SteamUsername = "Không có (Ẩn danh)";
                        profile.SteamPassword = "Không có (Ẩn danh)";
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