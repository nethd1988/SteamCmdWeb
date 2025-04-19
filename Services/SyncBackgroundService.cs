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
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(30); // Đồng bộ mỗi 30 phút

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

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Bắt đầu quét và đồng bộ tự động");
                    await _syncService.DiscoverAndSyncClientsAsync();
                    _logger.LogInformation("Hoàn thành quét và đồng bộ tự động");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi trong quá trình đồng bộ tự động");
                }

                // Đợi cho đến lần đồng bộ tiếp theo
                await Task.Delay(_syncInterval, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Dịch vụ đồng bộ tự động đang dừng");
            await base.StopAsync(cancellationToken);
        }
    }
}