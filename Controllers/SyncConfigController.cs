using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SteamCmdWeb.Services;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SyncConfigController : ControllerBase
    {
        private readonly ILogger<SyncConfigController> _logger;
        private readonly string _configPath;

        public SyncConfigController(ILogger<SyncConfigController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            string dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            _configPath = Path.Combine(dataFolder, "SyncConfig");

            if (!Directory.Exists(_configPath))
            {
                Directory.CreateDirectory(_configPath);
            }
        }

        [HttpGet]
        public IActionResult GetSyncConfig()
        {
            try
            {
                string configFilePath = Path.Combine(_configPath, "sync_config.json");
                if (!System.IO.File.Exists(configFilePath))
                {
                    var defaultConfig = new SyncConfig
                    {
                        EnableSilentSync = true,
                        SyncIntervalMinutes = 60,
                        MaxSyncSizeBytes = 50 * 1024 * 1024,
                        EnableAutoSync = true,
                        RequireAuthentication = true,
                        EnableDetailedLogging = true,
                        LastModified = DateTime.Now
                    };
                    return Ok(new { Config = defaultConfig, IsDefault = true });
                }

                string json = System.IO.File.ReadAllText(configFilePath);
                var config = JsonSerializer.Deserialize<SyncConfig>(json);
                return Ok(new { Config = config, IsDefault = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync config");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateSyncConfig([FromBody] SyncConfig config)
        {
            try
            {
                if (config == null)
                {
                    return BadRequest("Invalid config data");
                }

                if (config.SyncIntervalMinutes < 5)
                {
                    config.SyncIntervalMinutes = 5;
                }

                if (config.MaxSyncSizeBytes <= 0)
                {
                    config.MaxSyncSizeBytes = 50 * 1024 * 1024;
                }

                config.LastModified = DateTime.Now;

                string configFilePath = Path.Combine(_configPath, "sync_config.json");
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(configFilePath, json);

                _logger.LogInformation("Updated sync config: Silent sync {EnabledState}, Auto sync {AutoSyncState}",
                    config.EnableSilentSync ? "enabled" : "disabled",
                    config.EnableAutoSync ? "enabled" : "disabled");

                return Ok(new { Success = true, Message = "Sync config updated", Config = config });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating sync config");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet("logs")]
        public IActionResult GetSyncLogs(int count = 100)
        {
            try
            {
                string logPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");
                if (!Directory.Exists(logPath))
                {
                    return Ok(new List<object>());
                }

                var logFiles = new DirectoryInfo(logPath)
                    .GetFiles("silentsync_*.log")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(5)
                    .ToList();

                var logs = new List<object>();
                foreach (var file in logFiles)
                {
                    var fileContent = System.IO.File.ReadAllLines(file.FullName);

                    var recentLines = fileContent.Length <= count
                        ? fileContent
                        : fileContent.Skip(fileContent.Length - count).ToArray();

                    foreach (var line in recentLines)
                    {
                        var parts = line.Split(" - ", 3);
                        if (parts.Length >= 3)
                        {
                            logs.Add(new
                            {
                                Timestamp = parts[0],
                                ClientIp = parts[1],
                                Message = parts[2],
                                Source = file.Name
                            });
                        }
                    }
                }

                logs = logs.OrderByDescending(l => ((dynamic)l).Timestamp).Take(count).ToList();

                return Ok(new { Success = true, Logs = logs, TotalCount = logs.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync logs");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        [HttpDelete("logs")]
        public IActionResult ClearSyncLogs()
        {
            try
            {
                string logPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");
                if (!Directory.Exists(logPath))
                {
                    return Ok(new { Success = true, Message = "No logs to clear" });
                }

                var logFiles = new DirectoryInfo(logPath)
                    .GetFiles("silentsync_*.log")
                    .ToList();

                foreach (var file in logFiles)
                {
                    System.IO.File.Delete(file.FullName);
                }

                _logger.LogInformation("Cleared {Count} sync log files", logFiles.Count);
                return Ok(new { Success = true, Message = $"Cleared {logFiles.Count} log files" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing sync logs");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }
    }

    public class SyncConfig
    {
        public bool EnableSilentSync { get; set; } = true;
        public int SyncIntervalMinutes { get; set; } = 60;
        public long MaxSyncSizeBytes { get; set; } = 50 * 1024 * 1024;
        public bool EnableAutoSync { get; set; } = true;
        public bool RequireAuthentication { get; set; } = true;
        public bool EnableDetailedLogging { get; set; } = true;
        public DateTime LastModified { get; set; } = DateTime.Now;
        public List<string> AllowedIpAddresses { get; set; } = new List<string>();
    }
}