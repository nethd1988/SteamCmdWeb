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
                // Lấy kết quả đồng bộ gần đây
                SyncResults = _syncService.GetSyncResults();
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
                // Sử dụng phương thức tìm kiếm trên mạng rộng thay vì chỉ mạng cục bộ
                await _syncService.DiscoverAndSyncClientsAsync();

                StatusMessage = "Đã tìm kiếm và đồng bộ với các client SteamCmdWebAPI từ xa";
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

                StatusMessage = $"Đồng bộ hoàn tất. Thành công: {successCount}/{results.Count} clients, thêm {totalNewProfiles} profiles mới.";
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
    }
}