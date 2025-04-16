using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SystemStatusController : ControllerBase
    {
        private readonly ILogger<SystemStatusController> _logger;
        private readonly SystemMonitoringService _monitoringService;
        private readonly AppProfileManager _profileManager;
        private readonly string _dataPath;

        public SystemStatusController(
            ILogger<SystemStatusController> logger,
            SystemMonitoringService monitoringService,
            AppProfileManager profileManager)
        {
            _logger = logger;
            _monitoringService = monitoringService;
            _profileManager = profileManager;
            _dataPath = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        }

        /// <summary>
        /// Lấy thông tin trạng thái hệ thống
        /// </summary>
        [HttpGet]
        public IActionResult GetSystemStatus()
        {
            try
            {
                var overview = _monitoringService.GetSystemOverview();
                var profiles = _profileManager.GetAllProfiles();

                // Tính kích thước dữ liệu
                long dataFolderSize = GetDirectorySize(new DirectoryInfo(_dataPath));
                var dataSizeMB = dataFolderSize / (1024.0 * 1024);

                // Lấy thông tin version
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version.ToString();
                var buildDate = GetBuildDate(assembly);

                // Số lượng connections hiện tại
                var connections = GetTcpConnections();

                return Ok(new
                {
                    ServerInfo = new
                    {
                        ServerName = Environment.MachineName,
                        Version = version,
                        BuildDate = buildDate,
                        StartTime = overview.ProcessStartTime,
                        Uptime = $"{overview.ServerUptime.Days} days, {overview.ServerUptime.Hours} hours, {overview.ServerUptime.Minutes} minutes",
                        UptimeRaw = overview.ServerUptime,
                        Environment = HttpContext.Request.Headers["Host"].ToString().Contains("localhost") ? "Development" : "Production",
                        DataSizeMB = Math.Round(dataSizeMB, 2),
                        OperatingSystem = overview.OperatingSystem,
                        ProcessorCount = overview.ProcessorCount,
                        DotNetVersion = overview.DotNetVersion
                    },
                    ProfileStats = new
                    {
                        TotalProfiles = profiles.Count,
                        ActiveProfiles = profiles.Count(p => p.Status == "Running"),
                        AnonymousProfiles = profiles.Count(p => p.AnonymousLogin),
                        LastModified = profiles.Any() ? profiles.Max(p => p.LastRun) : (DateTime?)null
                    },
                    NetworkInfo = new
                    {
                        CurrentConnections = connections.Count,
                        LocalTcpPort = 61188,
                        ExternalPort = 61188,
                        ClientConnections = connections.Where(c => !c.LocalAddress.Contains("127.0.0.1")).Count()
                    },
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system status");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Lấy thông tin storage
        /// </summary>
        [HttpGet("storage")]
        public IActionResult GetStorageInfo()
        {
            try
            {
                // Lấy thông tin về thư mục dữ liệu
                var dataDir = new DirectoryInfo(_dataPath);
                var storageItems = new List<object>();

                // Thêm thông tin về các thư mục con
                foreach (var dir in dataDir.GetDirectories())
                {
                    long size = GetDirectorySize(dir);
                    storageItems.Add(new
                    {
                        Name = dir.Name,
                        Type = "Directory",
                        SizeBytes = size,
                        SizeMB = Math.Round(size / (1024.0 * 1024), 2),
                        LastModified = dir.LastWriteTime,
                        ItemCount = dir.GetFiles("*", SearchOption.AllDirectories).Length
                    });
                }

                // Thêm thông tin về các file trong thư mục gốc
                foreach (var file in dataDir.GetFiles())
                {
                    storageItems.Add(new
                    {
                        Name = file.Name,
                        Type = "File",
                        SizeBytes = file.Length,
                        SizeMB = Math.Round(file.Length / (1024.0 * 1024), 2),
                        LastModified = file.LastWriteTime
                    });
                }

                // Lấy thông tin ổ đĩa
                var drive = new DriveInfo(Path.GetPathRoot(dataDir.FullName));
                var totalSizeBytes = GetDirectorySize(dataDir);

                return Ok(new
                {
                    Success = true,
                    Path = _dataPath,
                    TotalSizeBytes = totalSizeBytes,
                    TotalSizeMB = Math.Round(totalSizeBytes / (1024.0 * 1024), 2),
                    DriveInfo = new
                    {
                        Name = drive.Name,
                        TotalSizeGB = Math.Round(drive.TotalSize / (1024.0 * 1024 * 1024), 2),
                        FreeSpaceGB = Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024 * 1024), 2),
                        UsedPercent = Math.Round((1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100, 2)
                    },
                    Items = storageItems.OrderByDescending(item => ((dynamic)item).SizeBytes)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting storage info");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Lấy thông tin về các kết nối hiện tại
        /// </summary>
        [HttpGet("connections")]
        public IActionResult GetConnectionInfo()
        {
            try
            {
                var connections = GetTcpConnections()
                    .Where(c => c.LocalPort == 61188 || c.RemotePort == 61188)
                    .ToList();

                return Ok(new
                {
                    Success = true,
                    TotalConnections = connections.Count,
                    ActiveClients = connections.Select(c => c.RemoteAddress).Distinct().Count(),
                    Connections = connections
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting connection info");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Tính kích thước thư mục
        /// </summary>
        private long GetDirectorySize(DirectoryInfo directory)
        {
            long size = 0;

            // Tính kích thước các file
            try
            {
                FileInfo[] files = directory.GetFiles();
                foreach (FileInfo file in files)
                {
                    size += file.Length;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Bỏ qua các thư mục không có quyền truy cập
            }

            // Tính kích thước các thư mục con
            try
            {
                DirectoryInfo[] subdirectories = directory.GetDirectories();
                foreach (DirectoryInfo subdirectory in subdirectories)
                {
                    size += GetDirectorySize(subdirectory);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Bỏ qua các thư mục không có quyền truy cập
            }

            return size;
        }

        /// <summary>
        /// Lấy ngày build của assembly
        /// </summary>
        private DateTime GetBuildDate(Assembly assembly)
        {
            try
            {
                const int peHeaderOffset = 60;
                const int linkerTimestampOffset = 8;

                byte[] bytes = new byte[2048];
                using (Stream stream = assembly.GetModules()[0].Assembly.Location == ""
                    ? new MemoryStream()
                    : new FileStream(assembly.GetModules()[0].Assembly.Location, FileMode.Open, FileAccess.Read))
                {
                    stream.Read(bytes, 0, bytes.Length);
                }

                int headerPos = BitConverter.ToInt32(bytes, peHeaderOffset);
                int secondsSince1970 = BitConverter.ToInt32(bytes, headerPos + linkerTimestampOffset);
                DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                dt = dt.AddSeconds(secondsSince1970);
                dt = dt.ToLocalTime();
                return dt;
            }
            catch
            {
                return DateTime.Now;
            }
        }

        /// <summary>
        /// Lấy thông tin các kết nối TCP
        /// </summary>
        private List<TcpConnectionInfo> GetTcpConnections()
        {
            var connections = new List<TcpConnectionInfo>();

            try
            {
                // Thực hiện lệnh netstat để lấy thông tin kết nối
                Process process = new Process();
                process.StartInfo.FileName = "netstat";
                process.StartInfo.Arguments = "-ano";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Phân tích kết quả
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("TCP"))
                    {
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            try
                            {
                                var localParts = parts[1].Split(':');
                                var remoteParts = parts[2].Split(':');

                                if (localParts.Length >= 2 && remoteParts.Length >= 2 &&
                                    int.TryParse(localParts[localParts.Length - 1], out int localPort) &&
                                    int.TryParse(remoteParts[remoteParts.Length - 1], out int remotePort))
                                {
                                    var connectionInfo = new TcpConnectionInfo
                                    {
                                        LocalAddress = string.Join(":", localParts.Take(localParts.Length - 1)),
                                        LocalPort = localPort,
                                        RemoteAddress = string.Join(":", remoteParts.Take(remoteParts.Length - 1)),
                                        RemotePort = remotePort,
                                        State = parts[3],
                                        ProcessId = parts.Length >= 5 ? int.Parse(parts[4]) : 0
                                    };

                                    connections.Add(connectionInfo);
                                }
                            }
                            catch
                            {
                                // Bỏ qua các dòng không phân tích được
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting TCP connections");
            }

            return connections;
        }
    }

    /// <summary>
    /// Thông tin kết nối TCP
    /// </summary>
    public class TcpConnectionInfo
    {
        public string LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public string RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public string State { get; set; }
        public int ProcessId { get; set; }
    }
}