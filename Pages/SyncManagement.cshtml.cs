using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;
using SteamCmdWeb.Models;

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

        public List<ClientRegistration> Clients { get; set; } = new List<ClientRegistration>();
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
                Clients = _syncService.GetRegisteredClients();
                // Lấy kết quả đồng bộ gần đây (thực tế sẽ cần lưu trữ và lấy từ service)
                SyncResults = new List<SyncResult>(); // Thay bằng lấy từ service
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải trang quản lý đồng bộ");
                StatusMessage = "Đã xảy ra lỗi khi tải trang: " + ex.Message;
                IsSuccess = false;
            }
        }

        public async Task<IActionResult> OnPostSyncClientAsync(string clientId)
        {
            try
            {
                var clients = _syncService.GetRegisteredClients();
                var client = clients.Find(c => c.ClientId == clientId);

                if (client == null)
                {
                    StatusMessage = "Không tìm thấy client với ID này";
                    IsSuccess = false;
                    return RedirectToPage();
                }

                var result = await _syncService.SyncProfilesFromClientAsync(client);

                StatusMessage = result.Success
                    ? $"Đồng bộ thành công từ client {clientId}. Đã thêm {result.NewProfilesAdded} profiles mới."
                    : $"Lỗi khi đồng bộ từ client {clientId}: {result.Message}";
                IsSuccess = result.Success;

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId}", clientId);
                StatusMessage = "Đã xảy ra lỗi khi đồng bộ: " + ex.Message;
                IsSuccess = false;
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostSyncAllAsync()
        {
            try
            {
                var results = await _syncService.SyncFromAllClientsAsync();

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
                _logger.LogError(ex, "Lỗi khi đồng bộ từ tất cả clients");
                StatusMessage = "Đã xảy ra lỗi khi đồng bộ từ tất cả clients: " + ex.Message;
                IsSuccess = false;
                return RedirectToPage();
            }
        }
    }
}