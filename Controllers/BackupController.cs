using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text.Json;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BackupController : ControllerBase
    {
        private readonly ILogger<BackupController> _logger;
        private readonly ProfileMigrationService _migrationService;
        private readonly AppProfileManager _appProfileManager;

        public BackupController(
            ILogger<BackupController> logger,
            ProfileMigrationService migrationService,
            AppProfileManager appProfileManager)
        {
            _logger = logger;
            _migrationService = migrationService;
            _appProfileManager = appProfileManager;
        }

        [HttpGet]
        public IActionResult GetBackups()
        {
            try
            {
                var backups = _migrationService.GetBackupFiles();
                return Ok(backups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting backup files");
                return StatusCode(500, new { Success = false, Message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateBackup([FromBody] List<ClientProfile> profiles)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    return BadRequest(new { Success = false, Message = "Không có profiles nào để backup" });
                }

                string result = await _migrationService.BackupClientProfiles(profiles);

                // Lưu lại thời gian tạo backup
                var now = DateTime.Now;
                var timestamp = now.ToString("dd/MM/yyyy HH:mm:ss");

                _logger.LogInformation("Created backup with {Count} profiles at {Timestamp}", profiles.Count, timestamp);

                return Ok(new
                {
                    Success = true,
                    Message = result,
                    Timestamp = timestamp,
                    ProfileCount = profiles.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating backup");
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpGet("load/{fileName}")]
        public async Task<IActionResult> LoadBackup(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return BadRequest(new { Success = false, Message = "Tên file không được để trống" });
                }

                // Kiểm tra file có tồn tại không
                var backups = _migrationService.GetBackupFiles();
                var backupFile = backups.FirstOrDefault(b => b.FileName == fileName);

                if (backupFile == null)
                {
                    return NotFound(new { Success = false, Message = $"Không tìm thấy file backup: {fileName}" });
                }

                // Thử đọc file trực tiếp nếu không thể đọc từ service
                try
                {
                    var profiles = await _migrationService.LoadProfilesFromBackup(fileName);

                    if (profiles == null || profiles.Count == 0)
                    {
                        return Ok(new { Success = true, Message = "Không có profiles nào trong file backup", Profiles = new List<ClientProfile>() });
                    }

                    _logger.LogInformation("Loaded {Count} profiles from backup file {FileName}", profiles.Count, fileName);

                    return Ok(profiles);
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "JSON parsing error for backup file {FileName}", fileName);
                    return StatusCode(500, new { Success = false, Message = $"Lỗi phân tích dữ liệu JSON: {jsonEx.Message}" });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading profiles from backup file {FileName}", fileName);
                    return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading backup file {FileName}", fileName);
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }

        [HttpPost("migrate")]
        public async Task<IActionResult> MigrateToAppProfiles([FromBody] List<ClientProfile> profiles, [FromQuery] bool skipDuplicateCheck = false)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    return BadRequest(new { Success = false, Message = "Không có profiles nào để di chuyển" });
                }

                var result = await _migrationService.MigrateProfilesToAppProfiles(profiles, skipDuplicateCheck);
                int added = result.Item1;
                int skipped = result.Item2;

                _logger.LogInformation("Migrated profiles to App Profiles: Added {Added}, Skipped {Skipped}", added, skipped);

                return Ok(new
                {
                    Success = true,
                    Message = $"Đã hoàn thành di chuyển. Đã thêm: {added}, Đã bỏ qua: {skipped}",
                    Added = added,
                    Skipped = skipped,
                    Total = profiles.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating profiles");
                return StatusCode(500, new { Success = false, Message = $"Lỗi: {ex.Message}" });
            }
        }
    }
}