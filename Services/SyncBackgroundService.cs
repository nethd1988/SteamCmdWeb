using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Services
{
    public class SyncBackgroundService : BackgroundService
    {
        private readonly ILogger<SyncBackgroundService> _logger;
        private readonly SyncService _syncService;

        // Đồng bộ mỗi 30 phút
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(30);

        public SyncBackgroundService(
            ILogger<SyncBackgroundService> logger,
            SyncService syncService)
        {
            _logger = logger;
            _syncService = syncService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dịch vụ đồng bộ tự động đã khởi động");

            try
            {
                // Khởi chạy đồng bộ đầu tiên sau 1 phút để đảm bảo hệ thống đã khởi động hoàn toàn
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                await PerformSyncAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi chạy đồng bộ đầu tiên");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Đợi cho đến thời điểm đồng bộ tiếp theo
                    await Task.Delay(_syncInterval, stoppingToken);

                    await PerformSyncAsync();
                }
                catch (OperationCanceledException)
                {
                    // Cancelled - shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi trong quá trình đồng bộ tự động");

                    // Đợi một khoảng thời gian ngắn trước khi thử lại để tránh spam lỗi
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("Dịch vụ đồng bộ tự động đã dừng");
        }

        private async Task PerformSyncAsync()
        {
            _logger.LogInformation("Đang chạy đồng bộ tự động định kỳ");

            try
            {
                var results = await _syncService.SyncFromAllKnownClientsAsync();

                int successCount = results.Count(r => r.Success);
                int totalAdded = results.Sum(r => r.NewProfilesAdded);

                _logger.LogInformation(
                    "Đồng bộ tự động hoàn tất. Thành công: {SuccessCount}/{Total}, Thêm mới: {Added} profiles",
                    successCount, results.Count, totalAdded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi thực hiện đồng bộ tự động");
            }
        }
    }
}