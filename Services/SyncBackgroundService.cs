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
            _logger.LogInformation("Dịch vụ đồng bộ tự động bắt đầu chạy");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Bắt đầu đồng bộ tự động với tất cả client đã biết");
                    await _syncService.SyncFromAllKnownClientsAsync();
                    _logger.LogInformation("Đồng bộ tự động hoàn tất. Tiếp theo sẽ chạy sau {Minutes} phút", _syncInterval.TotalMinutes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi trong quá trình đồng bộ tự động");
                }

                try
                {
                    await Task.Delay(_syncInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Dịch vụ đồng bộ tự động đã dừng");
        }
    }
}