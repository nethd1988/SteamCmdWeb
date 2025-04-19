using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;

namespace SteamCmdWeb.Services
{
    public class SyncBackgroundService : BackgroundService
    {
        private readonly ILogger<SyncBackgroundService> _logger;
        private readonly SyncService _syncService;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10);

        public SyncBackgroundService(
            ILogger<SyncBackgroundService> logger,
            SyncService syncService)
        {
            _logger = logger;
            _syncService = syncService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dịch vụ đồng bộ nền đã khởi động");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Kiểm tra các client mới trên mạng định kỳ
                    await _syncService.DiscoverAndSyncClientsAsync();
                    _logger.LogInformation("Đã hoàn thành kiểm tra đồng bộ tự động");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi trong quá trình đồng bộ tự động");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
    }
}