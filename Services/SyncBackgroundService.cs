using System;
using System.Diagnostics;
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
        private readonly TimeSpan _scanInterval = TimeSpan.FromHours(2);
        private DateTime _lastScanTime = DateTime.MinValue;

        public SyncBackgroundService(
            ILogger<SyncBackgroundService> logger,
            SyncService syncService)
        {
            _logger = logger;
            _syncService = syncService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dịch vụ đồng bộ nền đã bắt đầu. Sẽ đồng bộ mỗi {Minutes} phút", _syncInterval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Đợi ngẫu nhiên 0-30 giây khi khởi động để tránh tất cả các server cùng đồng bộ một lúc
                    if (DateTime.Now.Subtract(Process.GetCurrentProcess().StartTime).TotalMinutes < 5)
                    {
                        var random = new Random();
                        await Task.Delay(TimeSpan.FromSeconds(random.Next(0, 30)), stoppingToken);
                    }

                    // Quét mạng cục bộ theo định kỳ để tìm client mới
                    if ((DateTime.Now - _lastScanTime) > _scanInterval)
                    {
                        _logger.LogInformation("Bắt đầu quét mạng cục bộ để tìm client mới");
                        await _syncService.ScanLocalNetworkAsync();
                        _lastScanTime = DateTime.Now;
                    }

                    // Đồng bộ từ các client đã biết
                    _logger.LogInformation("Bắt đầu đồng bộ tự động từ tất cả client đã biết");
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

                    _logger.LogInformation("Đồng bộ tự động hoàn tất. Thành công: {SuccessCount}/{TotalCount} clients, thêm {NewProfiles} profiles mới",
                        successCount, results.Count, totalNewProfiles);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi trong quá trình đồng bộ tự động");
                }

                // Đợi cho đến khi đến lần đồng bộ tiếp theo
                try
                {
                    await Task.Delay(_syncInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Bỏ qua nếu bị hủy (dịch vụ đang dừng)
                    break;
                }
            }

            _logger.LogInformation("Dịch vụ đồng bộ nền đã dừng");
        }
    }
}