using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;
using SteamCmdWeb.Models;
using System.Linq;

namespace SteamCmdWeb.Pages
{
    public class SyncManagementModel : PageModel
    {
        private readonly ILogger<SyncManagementModel> _logger;
        private readonly SyncService _syncService;

        [TempData]
        public string StatusMessage { get; set; }

        [TempData]
        public bool IsSuccess { get; set; }

        public List<SyncResult> SyncResults { get; set; } = new List<SyncResult>();
        public List<ClientProfile> PendingProfiles { get; set; } = new List<ClientProfile>();

        public SyncManagementModel(
            ILogger<SyncManagementModel> logger,
            SyncService syncService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        }

        public void OnGet()
        {
            try
            {
                // Lấy kết quả đồng bộ gần đây (defensive copy to avoid concurrency issues)
                SyncResults = new List<SyncResult>(_syncService.GetSyncResults());

                // Lấy danh sách profile đang chờ (defensive copy)
                PendingProfiles = new List<ClientProfile>(_syncService.GetPendingProfiles());

                _logger.LogInformation("Đã tải trang quản lý đồng bộ với {ResultCount} kết quả và {PendingCount} profile đang chờ",
                    SyncResults.Count, PendingProfiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải trang quản lý đồng bộ");
                StatusMessage = "Đã xảy ra lỗi khi tải trang: " + ex.Message;
                IsSuccess = false;
            }
        }

        public async Task<IActionResult> OnPostScanNetworkAsync()
        {
            try
            {
                await _syncService.DiscoverAndSyncClientsAsync();

                StatusMessage = "Đã tìm kiếm và đồng bộ với các client SteamCmdWebAPI trên mạng";
                IsSuccess = true;

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tìm kiếm client");
                StatusMessage = "Đã xảy ra lỗi khi tìm kiếm client: " + ex.Message;
                IsSuccess = false;
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostSyncKnownClientsAsync()
        {
            try
            {
                var results = await _syncService.SyncFromAllKnownClientsAsync();

                int successCount = 0;
                int totalNewProfiles = 0;

                foreach (var result in results)
                {
                    if (result.Success)
                    {
                        successCount++;
                        totalNewProfiles += result.NewProfilesAdded;
                    }
                }

                StatusMessage = $"Đồng bộ hoàn tất. Thành công: {successCount}/{results.Count} clients, thêm {totalNewProfiles} profiles vào danh sách chờ.";
                IsSuccess = true;

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ các client đã biết");
                StatusMessage = "Đã xảy ra lỗi khi đồng bộ từ các client đã biết: " + ex.Message;
                IsSuccess = false;
                return RedirectToPage();
            }
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