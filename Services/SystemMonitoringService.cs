using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SteamCmdWeb.Services
{
    public class SystemMonitoringService : BackgroundService
    {
        private readonly ILogger<SystemMonitoringService> _logger;
        private readonly DateTime _processStartTime = DateTime.Now;

        public SystemMonitoringService(ILogger<SystemMonitoringService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("System Monitoring Service đã khởi động");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); // Kiểm tra mỗi 15 phút
                    LogSystemStatus();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Khi service đang dừng, bỏ qua lỗi
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi ghi log trạng thái hệ thống");
                }
            }
        }

        public SystemOverview GetSystemOverview()
        {
            var overview = new SystemOverview
            {
                ServerUptime = DateTime.Now - _processStartTime,
                ProcessStartTime = _processStartTime,
                ProcessorCount = Environment.ProcessorCount,
                WorkingSet = Process.GetCurrentProcess().WorkingSet64,
                OperatingSystem = GetOperatingSystemInfo(),
                DotNetVersion = Environment.Version.ToString(),
                MachineName = Environment.MachineName
            };

            // Thêm thông tin đĩa
            try
            {
                var currentDir = Directory.GetCurrentDirectory();
                var driveInfo = new DriveInfo(Path.GetPathRoot(currentDir));
                overview.DriveTotalSpace = driveInfo.TotalSize;
                overview.DriveAvailableSpace = driveInfo.AvailableFreeSpace;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể lấy thông tin ổ đĩa");
            }

            return overview;
        }

        private string GetOperatingSystemInfo()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return $"Windows {Environment.OSVersion.Version}";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return $"Linux {Environment.OSVersion.Version}";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return $"macOS {Environment.OSVersion.Version}";
            }
            else
            {
                return Environment.OSVersion.ToString();
            }
        }

        private void LogSystemStatus()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var uptime = DateTime.Now - _processStartTime;
                var workingSet = process.WorkingSet64 / (1024 * 1024); // MB

                _logger.LogInformation(
                    "System Status - Uptime: {Uptime}, Working Set: {WorkingSet} MB, Threads: {Threads}, Handles: {Handles}",
                    $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
                    workingSet,
                    process.Threads.Count,
                    process.HandleCount);

                // Kiểm tra thư mục data
                var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
                if (Directory.Exists(dataFolder))
                {
                    long dataSize = 0;
                    var directory = new DirectoryInfo(dataFolder);
                    foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
                    {
                        dataSize += file.Length;
                    }

                    _logger.LogInformation("Data folder size: {Size} MB", dataSize / (1024 * 1024));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi ghi log trạng thái hệ thống");
            }
        }
    }

    public class SystemOverview
    {
        public TimeSpan ServerUptime { get; set; }
        public DateTime ProcessStartTime { get; set; }
        public int ProcessorCount { get; set; }
        public long WorkingSet { get; set; }
        public string OperatingSystem { get; set; }
        public string DotNetVersion { get; set; }
        public string MachineName { get; set; }
        public long DriveTotalSpace { get; set; }
        public long DriveAvailableSpace { get; set; }
    }
}