// Phần này sửa lỗi trong phương thức CollectMetric của SystemMonitoringService.cs

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

        try {
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