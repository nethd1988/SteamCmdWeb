using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SteamCmdWeb.Services
{
    /// <summary>
    /// Lớp dịch vụ giám sát hệ thống, thu thập thông tin về CPU, bộ nhớ và ổ đĩa
    /// </summary>
    public class SystemMonitoringService : BackgroundService
    {
        private readonly ILogger<SystemMonitoringService> _logger;
        private readonly ConcurrentQueue<SystemMetric> _metrics = new ConcurrentQueue<SystemMetric>();
        private readonly Process _currentProcess;
        private readonly int _maxMetrics = 10000;
        private readonly string _monitoringDataFolder;

        // Thông tin tổng quan hệ thống
        private readonly SystemOverview _systemOverview = new SystemOverview();

        /// <summary>
        /// Khởi tạo dịch vụ giám sát hệ thống
        /// </summary>
        public SystemMonitoringService(ILogger<SystemMonitoringService> logger = null)
        {
            _logger = logger;
            _currentProcess = Process.GetCurrentProcess();

            // Khởi tạo thông tin hệ thống
            _systemOverview.ProcessStartTime = _currentProcess.StartTime;
            _systemOverview.HostName = Environment.MachineName;
            _systemOverview.OperatingSystem = RuntimeInformation.OSDescription;
            _systemOverview.ProcessorCount = Environment.ProcessorCount;
            _systemOverview.DotNetVersion = RuntimeInformation.FrameworkDescription;

            // Tạo thư mục lưu dữ liệu giám sát
            string baseDir = Directory.GetCurrentDirectory();
            _monitoringDataFolder = Path.Combine(baseDir, "Data", "Monitoring");
            if (!Directory.Exists(_monitoringDataFolder))
            {
                Directory.CreateDirectory(_monitoringDataFolder);
                _logger?.LogInformation("Đã tạo thư mục dữ liệu giám sát: {Path}", _monitoringDataFolder);
            }

            _logger?.LogInformation("Dịch vụ giám sát hệ thống đã được khởi tạo");
        }

        /// <summary>
        /// Chạy dịch vụ
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger?.LogInformation("Dịch vụ giám sát hệ thống đã bắt đầu");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Cập nhật thời gian uptime
                    _systemOverview.ServerUptime = DateTime.Now - _systemOverview.ProcessStartTime;

                    // Đợi 30 giây trước khi thu thập tiếp
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Hoạt động bình thường khi dịch vụ bị hủy
                _logger?.LogInformation("Dịch vụ giám sát hệ thống đã bị hủy");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi trong dịch vụ giám sát hệ thống: {Message}", ex.Message);
            }
            finally
            {
                _logger?.LogInformation("Dịch vụ giám sát hệ thống đã dừng");
            }
        }

        /// <summary>
        /// Lấy thông tin tổng quan hệ thống
        /// </summary>
        public SystemOverview GetSystemOverview()
        {
            return _systemOverview;
        }

        /// <summary>
        /// Lấy danh sách metrics hiện tại
        /// </summary>
        public List<SystemMetric> GetCurrentMetrics(int maxPoints = 100)
        {
            try
            {
                return _metrics.Reverse().Take(maxPoints).ToList();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi lấy danh sách metrics hiện tại");
                return new List<SystemMetric>();
            }
        }
    }

    /// <summary>
    /// Lớp lưu trữ thông tin metric hệ thống tại một thời điểm
    /// </summary>
    public class SystemMetric
    {
        /// <summary>
        /// Thời điểm ghi nhận
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// ID tiến trình
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// Phần trăm CPU được sử dụng bởi tiến trình
        /// </summary>
        public double ProcessCpuUsage { get; set; }

        /// <summary>
        /// Bộ nhớ sử dụng bởi tiến trình (MB)
        /// </summary>
        public double ProcessMemoryUsageMB { get; set; }

        /// <summary>
        /// Phần trăm CPU sử dụng của toàn hệ thống
        /// </summary>
        public double SystemCpuUsage { get; set; }

        /// <summary>
        /// Bộ nhớ khả dụng của hệ thống (MB)
        /// </summary>
        public double SystemAvailableMemoryMB { get; set; }

        /// <summary>
        /// Tổng dung lượng ổ đĩa (GB)
        /// </summary>
        public double DiskTotalGB { get; set; }

        /// <summary>
        /// Dung lượng ổ đĩa còn trống (GB)
        /// </summary>
        public double DiskFreeGB { get; set; }

        /// <summary>
        /// Phần trăm sử dụng ổ đĩa
        /// </summary>
        public double DiskUsagePercent { get; set; }
    }

    /// <summary>
    /// Lớp lưu trữ thông tin tổng quan hệ thống
    /// </summary>
    public class SystemOverview
    {
        /// <summary>
        /// Thời điểm tiến trình bắt đầu
        /// </summary>
        public DateTime ProcessStartTime { get; set; }

        /// <summary>
        /// Thời gian chạy của server
        /// </summary>
        public TimeSpan ServerUptime { get; set; }

        /// <summary>
        /// Tên máy chủ
        /// </summary>
        public string HostName { get; set; }

        /// <summary>
        /// Hệ điều hành
        /// </summary>
        public string OperatingSystem { get; set; }

        /// <summary>
        /// Số lượng CPU
        /// </summary>
        public int ProcessorCount { get; set; }

        /// <summary>
        /// Phiên bản .NET
        /// </summary>
        public string DotNetVersion { get; set; }

        /// <summary>
        /// Metric hiện tại
        /// </summary>
        public SystemMetric CurrentMetric { get; set; }
    }
}