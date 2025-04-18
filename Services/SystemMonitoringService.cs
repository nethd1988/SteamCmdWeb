using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SteamCmdWeb.Services
{
    public class SystemMonitoringService : BackgroundService
    {
        private readonly ILogger<SystemMonitoringService> _logger;
        private readonly Process _currentProcess;
        private readonly DateTime _startTime;
        private readonly string _dotNetVersion;
        private readonly string _osVersion;
        private readonly int _processorCount;

        public SystemMonitoringService(ILogger<SystemMonitoringService> logger)
        {
            _logger = logger;
            _currentProcess = Process.GetCurrentProcess();
            _startTime = _currentProcess.StartTime;
            _dotNetVersion = Environment.Version.ToString();
            _osVersion = RuntimeInformation.OSDescription;
            _processorCount = Environment.ProcessorCount;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Dịch vụ giám sát hệ thống đã khởi động");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Ghi log thông tin hệ thống mỗi giờ
                    var overview = GetSystemOverview();
                    _logger.LogInformation("Thông tin hệ thống - Uptime: {Uptime}, CPU: {CPU}%, RAM: {RAM}MB",
                        overview.ServerUptime, overview.CpuUsage, overview.MemoryUsageMB);

                    // Chờ 1 giờ trước khi kiểm tra lại
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Bình thường khi dừng dịch vụ
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong dịch vụ giám sát hệ thống");
            }
        }

        public SystemOverview GetSystemOverview()
        {
            try
            {
                _currentProcess.Refresh();

                return new SystemOverview
                {
                    ProcessStartTime = _startTime,
                    ServerUptime = DateTime.Now - _startTime,
                    CpuUsage = GetCpuUsage(),
                    MemoryUsageMB = _currentProcess.WorkingSet64 / (1024 * 1024),
                    ThreadCount = _currentProcess.Threads.Count,
                    DotNetVersion = _dotNetVersion,
                    OperatingSystem = _osVersion,
                    ProcessorCount = _processorCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy thông tin hệ thống");

                return new SystemOverview
                {
                    ProcessStartTime = _startTime,
                    ServerUptime = DateTime.Now - _startTime,
                    CpuUsage = 0,
                    MemoryUsageMB = 0,
                    ThreadCount = 0,
                    DotNetVersion = _dotNetVersion,
                    OperatingSystem = _osVersion,
                    ProcessorCount = _processorCount
                };
            }
        }

        private double GetCpuUsage()
        {
            try
            {
                var startCpuUsage = _currentProcess.TotalProcessorTime;
                var startTime = DateTime.UtcNow;

                // Chờ 100ms để đo
                Thread.Sleep(100);

                _currentProcess.Refresh();
                var endCpuUsage = _currentProcess.TotalProcessorTime;
                var endTime = DateTime.UtcNow;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;

                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

                return Math.Round(cpuUsageTotal * 100, 2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tính toán CPU usage");
                return 0;
            }
        }
    }

    public class SystemOverview
    {
        public DateTime ProcessStartTime { get; set; }
        public TimeSpan ServerUptime { get; set; }
        public double CpuUsage { get; set; }
        public long MemoryUsageMB { get; set; }
        public int ThreadCount { get; set; }
        public string DotNetVersion { get; set; }
        public string OperatingSystem { get; set; }
        public int ProcessorCount { get; set; }
    }
}