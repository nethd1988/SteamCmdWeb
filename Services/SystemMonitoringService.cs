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
    /// Lớp dịch vụ giám sát hệ thống, thu thập thông tin về CPU, bộ nhớ và ổ đĩa.
    /// Hoạt động như một dịch vụ nền (BackgroundService) trong ASP.NET Core.
    /// </summary>
    public class SystemMonitoringService : BackgroundService
    {
        private readonly ILogger<SystemMonitoringService> _logger;
        private readonly ConcurrentQueue<SystemMetric> _metrics = new ConcurrentQueue<SystemMetric>();
        private readonly Process _currentProcess;
        private readonly int _maxMetrics = 10000; // Giữ tối đa 10000 điểm dữ liệu
        private readonly string _monitoringDataFolder;

        private double _lastProcessTotalTime = 0;
        private DateTime _lastProcessTimeSnapshot = DateTime.MinValue;

        private double _lastSystemTotalTime = 0;
        private DateTime _lastSystemTimeSnapshot = DateTime.MinValue;

        // Thông tin tổng quan hệ thống
        private readonly SystemOverview _systemOverview = new SystemOverview();

        /// <summary>
        /// Khởi tạo dịch vụ giám sát hệ thống
        /// </summary>
        public SystemMonitoringService(ILogger<SystemMonitoringService> logger = null)
        {
            _logger = logger;

            // Lấy process hiện tại
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

            // Ghi dữ liệu hệ thống ban đầu
            await LogSystemInfo();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Thu thập metric
                    CollectMetric();

                    // Cập nhật thông tin tổng quan
                    UpdateSystemOverview();

                    // Ghi log mỗi 10 phút (hoặc bạn có thể điều chỉnh tần suất tùy thích)
                    if (DateTime.Now.Minute % 10 == 0 && DateTime.Now.Second < 10)
                    {
                        await LogCurrentMetrics();
                    }

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
        /// Thu thập các thông số hệ thống hiện tại
        /// </summary>
        private void CollectMetric()
        {
            try
            {
                var metric = new SystemMetric
                {
                    Timestamp = DateTime.Now,
                    ProcessId = _currentProcess.Id
                };

                // Cập nhật process để lấy thông tin mới nhất
                _currentProcess.Refresh();

                // Thu thập thông tin process
                metric.ProcessCpuUsage = GetProcessCpuUsage();
                metric.ProcessMemoryUsageMB = _currentProcess.WorkingSet64 / (1024 * 1024);
                metric.ProcessThreadCount = _currentProcess.Threads.Count;
                metric.ProcessHandleCount = _currentProcess.HandleCount;

                // Thu thập thông tin hệ thống
                metric.SystemCpuUsage = GetSystemCpuUsage();
                metric.SystemAvailableMemoryMB = GetSystemAvailableMemory();

                try
                {
                    // Thu thập thông tin disk
                    string rootPath = Path.GetPathRoot(Directory.GetCurrentDirectory());
                    if (!string.IsNullOrEmpty(rootPath))
                    {
                        var currentDrive = new DriveInfo(rootPath);
                        if (currentDrive.IsReady)
                        {
                            metric.DiskTotalGB = currentDrive.TotalSize / (1024.0 * 1024 * 1024);
                            metric.DiskFreeGB = currentDrive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                            metric.DiskUsagePercent = 100 - (metric.DiskFreeGB / metric.DiskTotalGB * 100);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Không thể lấy thông tin ổ đĩa");
                    // Đặt giá trị mặc định
                    metric.DiskTotalGB = 100;
                    metric.DiskFreeGB = 50;
                    metric.DiskUsagePercent = 50;
                }

                // Thêm metric vào queue
                _metrics.Enqueue(metric);

                // Giới hạn số lượng metrics
                while (_metrics.Count > _maxMetrics)
                {
                    _metrics.TryDequeue(out _);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi thu thập metric hệ thống");
            }
        }

        /// <summary>
        /// Lấy mức sử dụng CPU của tiến trình hiện tại
        /// </summary>
        private double GetProcessCpuUsage()
        {
            try
            {
                DateTime now = DateTime.Now;
                TimeSpan totalProcessorTime = _currentProcess.TotalProcessorTime;

                if (_lastProcessTimeSnapshot == DateTime.MinValue)
                {
                    _lastProcessTimeSnapshot = now;
                    _lastProcessTotalTime = totalProcessorTime.TotalMilliseconds;
                    return 0;
                }

                double currentProcessCpuUsage = (totalProcessorTime.TotalMilliseconds - _lastProcessTotalTime) /
                                              (Environment.ProcessorCount * (now - _lastProcessTimeSnapshot).TotalMilliseconds) * 100;

                _lastProcessTimeSnapshot = now;
                _lastProcessTotalTime = totalProcessorTime.TotalMilliseconds;

                return Math.Max(0, Math.Min(100, currentProcessCpuUsage));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Không thể lấy mức sử dụng CPU của process");
                return 0;
            }
        }

        /// <summary>
        /// Lấy mức sử dụng CPU của hệ thống
        /// </summary>
        private double GetSystemCpuUsage()
        {
            try
            {
                // Trên Windows, chúng ta có thể sử dụng PerformanceCounter nhưng điều này phụ thuộc vào nền tảng
                // Đây là một cách đơn giản hơn hoạt động trên nhiều nền tảng
                string[] cpuLoadAvg = null;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    try
                    {
                        // Đọc /proc/loadavg trên Linux
                        string loadAvg = File.ReadAllText("/proc/loadavg");
                        cpuLoadAvg = loadAvg.Split(' ');
                    }
                    catch
                    {
                        // Fallback nếu không thể đọc file
                        cpuLoadAvg = new[] { "0", "0", "0" };
                    }

                    if (cpuLoadAvg.Length >= 3 && double.TryParse(cpuLoadAvg[0], out double oneMinLoad))
                    {
                        // Chuyển đổi load average sang phần trăm (load average 1 tương đương 100% trên 1 core)
                        return Math.Min(100, (oneMinLoad / Environment.ProcessorCount) * 100);
                    }

                    return 0;
                }
                else
                {
                    // Giả lập giá trị CPU cho các nền tảng khác (nên sử dụng thư viện như LibraryNative để thực hiện chính xác hơn)
                    DateTime now = DateTime.Now;

                    if (_lastSystemTimeSnapshot == DateTime.MinValue)
                    {
                        _lastSystemTimeSnapshot = now;
                        return 0;
                    }

                    // Mô phỏng giá trị CPU từ process hiện tại
                    double systemCpuUsage = GetProcessCpuUsage() * 2;  // Giả định hệ thống sử dụng gấp đôi process

                    _lastSystemTimeSnapshot = now;

                    return Math.Max(0, Math.Min(100, systemCpuUsage));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Không thể lấy mức sử dụng CPU của hệ thống");
                return 0;
            }
        }

        /// <summary>
        /// Lấy lượng bộ nhớ khả dụng của hệ thống (MB)
        /// </summary>
        private double GetSystemAvailableMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    try
                    {
                        // Đọc /proc/meminfo trên Linux
                        string memInfo = File.ReadAllText("/proc/meminfo");
                        string[] lines = memInfo.Split('\n');

                        long totalMemKB = 0;
                        long availableMemKB = 0;

                        foreach (string line in lines)
                        {
                            if (line.StartsWith("MemTotal:"))
                            {
                                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2 && long.TryParse(parts[1], out long total))
                                {
                                    totalMemKB = total;
                                }
                            }
                            else if (line.StartsWith("MemAvailable:"))
                            {
                                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2 && long.TryParse(parts[1], out long available))
                                {
                                    availableMemKB = available;
                                }
                            }
                        }

                        if (totalMemKB > 0 && availableMemKB > 0)
                        {
                            return availableMemKB / 1024.0; // Chuyển KB sang MB
                        }
                    }
                    catch
                    {
                        // Fallback nếu không thể đọc file
                    }
                }

                // Giả lập giá trị bộ nhớ khả dụng cho các nền tảng khác
                return 4096; // Giả định 4GB RAM khả dụng
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Không thể lấy thông tin bộ nhớ khả dụng của hệ thống");
                return 1024; // Trả về giá trị mặc định 1GB
            }
        }

        /// <summary>
        /// Cập nhật thông tin tổng quan hệ thống
        /// </summary>
        private void UpdateSystemOverview()
        {
            try
            {
                // Cập nhật thời gian uptime
                _systemOverview.ServerUptime = DateTime.Now - _systemOverview.ProcessStartTime;

                // Lấy metric mới nhất
                SystemMetric latestMetric = null;
                if (_metrics.TryPeek(out latestMetric))
                {
                    _systemOverview.CurrentMetric = latestMetric;

                    // Cập nhật các số liệu thống kê
                    if (latestMetric.SystemCpuUsage > _systemOverview.PeakSystemCpuUsage)
                    {
                        _systemOverview.PeakSystemCpuUsage = latestMetric.SystemCpuUsage;
                    }

                    if (latestMetric.ProcessMemoryUsageMB > _systemOverview.PeakProcessMemoryUsageMB)
                    {
                        _systemOverview.PeakProcessMemoryUsageMB = latestMetric.ProcessMemoryUsageMB;
                    }

                    // Cập nhật trung bình sử dụng CPU
                    var recentMetrics = GetCurrentMetrics(10); // Lấy 10 mẫu gần nhất
                    if (recentMetrics.Count > 0)
                    {
                        _systemOverview.AverageSystemCpuUsage = recentMetrics.Average(m => m.SystemCpuUsage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi cập nhật thông tin tổng quan hệ thống");
            }
        }

        /// <summary>
        /// Ghi log thông tin hệ thống ban đầu
        /// </summary>
        private async Task LogSystemInfo()
        {
            try
            {
                string filePath = Path.Combine(_monitoringDataFolder, "system_info.log");

                var info = new
                {
                    Timestamp = DateTime.Now,
                    MachineName = Environment.MachineName,
                    OSVersion = RuntimeInformation.OSDescription,
                    ProcessorCount = Environment.ProcessorCount,
                    RuntimeVersion = RuntimeInformation.FrameworkDescription,
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    ProcessId = _currentProcess.Id,
                    ProcessName = _currentProcess.ProcessName,
                    ProcessStartTime = _currentProcess.StartTime
                };

                string content = $"=== System Information ({DateTime.Now}) ===\n" +
                                $"Machine Name: {info.MachineName}\n" +
                                $"OS Version: {info.OSVersion}\n" +
                                $"Processor Count: {info.ProcessorCount}\n" +
                                $"Runtime Version: {info.RuntimeVersion}\n" +
                                $"Working Directory: {info.WorkingDirectory}\n" +
                                $"Process ID: {info.ProcessId}\n" +
                                $"Process Name: {info.ProcessName}\n" +
                                $"Process Start Time: {info.ProcessStartTime}\n";

                await File.WriteAllTextAsync(filePath, content);
                _logger?.LogInformation("Đã ghi thông tin hệ thống vào file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi ghi log thông tin hệ thống");
            }
        }

        /// <summary>
        /// Ghi log metrics hiện tại
        /// </summary>
        private async Task LogCurrentMetrics()
        {
            try
            {
                if (_metrics.IsEmpty) return;

                var recentMetrics = GetCurrentMetrics(10); // Lấy 10 mẫu gần nhất
                if (recentMetrics.Count == 0) return;

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
                string filePath = Path.Combine(_monitoringDataFolder, $"metrics_{timestamp}.log");

                // Lấy metric mới nhất và tính toán một số thống kê
                var latestMetric = recentMetrics.Last();
                var avgCpuUsage = recentMetrics.Average(m => m.SystemCpuUsage);
                var avgMemoryUsage = recentMetrics.Average(m => m.ProcessMemoryUsageMB);

                string content = $"=== System Metrics ({DateTime.Now}) ===\n" +
                                $"Current CPU Usage: {latestMetric.SystemCpuUsage:F2}%\n" +
                                $"Average CPU Usage: {avgCpuUsage:F2}%\n" +
                                $"Current Memory Usage: {latestMetric.ProcessMemoryUsageMB:F0} MB\n" +
                                $"Average Memory Usage: {avgMemoryUsage:F0} MB\n" +
                                $"Available System Memory: {latestMetric.SystemAvailableMemoryMB:F0} MB\n" +
                                $"Disk Free Space: {latestMetric.DiskFreeGB:F1} GB\n" +
                                $"Disk Usage: {latestMetric.DiskUsagePercent:F1}%\n" +
                                $"Thread Count: {latestMetric.ProcessThreadCount}\n" +
                                $"Handle Count: {latestMetric.ProcessHandleCount}\n";

                await File.WriteAllTextAsync(filePath, content);
                _logger?.LogDebug("Đã ghi metrics hiện tại vào file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi khi ghi log metrics hiện tại");
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
        /// Số lượng thread trong tiến trình
        /// </summary>
        public int ProcessThreadCount { get; set; }

        /// <summary>
        /// Số lượng handle trong tiến trình
        /// </summary>
        public int ProcessHandleCount { get; set; }

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

        /// <summary>
        /// Mức sử dụng CPU cao nhất
        /// </summary>
        public double PeakSystemCpuUsage { get; set; }

        /// <summary>
        /// Mức sử dụng bộ nhớ cao nhất (MB)
        /// </summary>
        public double PeakProcessMemoryUsageMB { get; set; }

        /// <summary>
        /// Mức sử dụng CPU trung bình
        /// </summary>
        public double AverageSystemCpuUsage { get; set; }
    }
}