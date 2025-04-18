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

        // Khoảng thời gian đồng bộ mặc định: 30 phút
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(30);
        // Khoảng thời gian phát hiện client mới: 6 giờ
        private readonly TimeSpan _discoverInterval = TimeSpan.FromHours(6);

        public SyncBackgroundService(
            ILogger<SyncBackgroundService> logger,
            SyncService syncService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Sync Background Service đã khởi động");

            // Chạy đồng bộ lần đầu sau 1 phút khởi động
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            // Thực hiện phát hiện tự động lần đầu
            await _syncService.AutoDiscoverAndRegisterClientsAsync();

            // Thực hiện đồng bộ lần đầu
            await _syncService.AutoSyncAsync();

            DateTime lastDiscoverTime = DateTime.Now;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Kiểm tra xem có cần phát hiện client mới không
                    if ((DateTime.Now - lastDiscoverTime) >= _discoverInterval)
                    {
                        _logger.LogInformation("Đang thực hiện phát hiện tự động client");
                        await _syncService.AutoDiscoverAndRegisterClientsAsync();
                        lastDiscoverTime = DateTime.Now;
                    }

                    // Thực hiện đồng bộ tự động
                    _logger.LogInformation("Đang thực hiện đồng bộ tự động");
                    await _syncService.AutoSyncAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi trong quá trình đồng bộ tự động");
                }

                _logger.LogInformation("Đồng bộ hoàn tất. Đợi {Minutes} phút cho lần tiếp theo",
                    _syncInterval.TotalMinutes);

                // Đợi đến lần đồng bộ tiếp theo
                await Task.Delay(_syncInterval, stoppingToken);
            }
        }
    }
}