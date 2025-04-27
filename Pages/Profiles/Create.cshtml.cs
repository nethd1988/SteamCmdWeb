using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SteamCmdWeb.Pages.Profiles
{
    public class CreateModel : PageModel
    {
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;
        private readonly ILogger<CreateModel> _logger;

        [BindProperty]
        public ClientProfile Profile { get; set; } = new ClientProfile();

        [BindProperty]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        [TempData]
        public string SuccessMessage { get; set; }

        public string ErrorMessage { get; set; }

        public CreateModel(
            ProfileService profileService,
            DecryptionService decryptionService,
            ILogger<CreateModel> logger)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void OnGet()
        {
            // Khởi tạo profile mới với giá trị mặc định
            Profile = new ClientProfile
            {
                Status = "Ready",
                StartTime = DateTime.Now,
                StopTime = DateTime.Now,
                LastRun = DateTime.UtcNow
            };
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = string.Join("; ", ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));
                    ErrorMessage = $"Thông tin không hợp lệ: {errors}";
                    return Page();
                }

                // Mã hóa thông tin đăng nhập
                if (!string.IsNullOrEmpty(Username))
                {
                    Profile.SteamUsername = _decryptionService.EncryptString(Username);
                }

                if (!string.IsNullOrEmpty(Password))
                {
                    Profile.SteamPassword = _decryptionService.EncryptString(Password);
                }

                // Đặt trạng thái mặc định
                Profile.Status = "Ready";
                Profile.StartTime = DateTime.Now;
                Profile.StopTime = DateTime.Now;
                Profile.LastRun = DateTime.UtcNow;
                Profile.Pid = 0;

                // Tạo profile mới
                var newProfile = await _profileService.AddProfileAsync(Profile);
                _logger.LogInformation("Đã tạo profile mới: {0} (ID: {1})", newProfile.Name, newProfile.Id);

                SuccessMessage = $"Đã tạo profile '{newProfile.Name}' thành công.";
                return RedirectToPage("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo profile mới");
                ErrorMessage = $"Đã xảy ra lỗi khi tạo profile: {ex.Message}";
                return Page();
            }
        }

        // Handler để scan games từ thư mục
        public async Task<IActionResult> OnGetScanGamesAsync()
        {
            try
            {
                var scannedGames = new List<object>();
                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = existingProfiles.Select(p => p.AppID).ToList();
                var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Quét tất cả ổ đĩa cố định
                var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady);

                foreach (var drive in drives)
                {
                    // Đường dẫn Steam cơ bản cần kiểm tra
                    var steamPaths = new[]
                    {
                        Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Steam"),
                        Path.Combine(drive.RootDirectory.FullName, "Program Files", "Steam"),
                        Path.Combine(drive.RootDirectory.FullName, "Steam"),
                        Path.Combine(drive.RootDirectory.FullName, "Games", "Steam"),
                        Path.Combine(drive.RootDirectory.FullName, "Games"),
                        Path.Combine(drive.RootDirectory.FullName, "SteamLibrary"),
                        Path.Combine(drive.RootDirectory.FullName, "Online Games"),
                    };

                    foreach (var steamPath in steamPaths)
                    {
                        if (Directory.Exists(steamPath))
                        {
                            await ScanDirectoryForGamesAsync(steamPath, scannedGames, existingAppIds, processedPaths);
                        }
                    }

                    // Quét thư mục gốc của ổ đĩa (nếu là ổ SSD nhỏ)
                    const long maxSizeToScanInBytes = 240L * 1024 * 1024 * 1024; // 240GB
                    try
                    {
                        if (drive.TotalSize < maxSizeToScanInBytes)
                        {
                            await ScanDirectoryForGamesAsync(drive.RootDirectory.FullName, scannedGames, existingAppIds, processedPaths, 1);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Không thể quét thư mục gốc của ổ đĩa {DriveName}", drive.Name);
                        // Tiếp tục quét các ổ đĩa khác
                    }
                }

                if (scannedGames.Count == 0)
                {
                    return new JsonResult(new { success = false, message = "Không tìm thấy game nào hoặc tất cả game đã có profile" });
                }

                return new JsonResult(new { success = true, games = scannedGames });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi scan games");
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // Phương thức quét đệ quy
        private async Task ScanDirectoryForGamesAsync(string directory, List<object> scannedGames, List<string> existingAppIds, HashSet<string> processedPaths, int maxDepth = 2, int currentDepth = 0)
        {
            if (currentDepth > maxDepth || !Directory.Exists(directory) || processedPaths.Contains(directory))
                return;

            processedPaths.Add(directory);

            try
            {
                // Kiểm tra thư mục steamapps trong thư mục hiện tại
                var steamappsDir = Path.Combine(directory, "steamapps");
                if (Directory.Exists(steamappsDir))
                {
                    // Tìm tất cả file appmanifest_*.acf
                    var manifestFiles = Directory.GetFiles(steamappsDir, "appmanifest_*.acf");

                    foreach (var manifestFile in manifestFiles)
                    {
                        var match = Regex.Match(Path.GetFileName(manifestFile), @"appmanifest_(\d+)\.acf");
                        if (match.Success)
                        {
                            string appId = match.Groups[1].Value;

                            // Bỏ qua nếu đã có profile cho game này
                            if (existingAppIds.Contains(appId))
                                continue;

                            // Đọc file manifest
                            string content = await System.IO.File.ReadAllTextAsync(manifestFile);

                            // Lấy tên game từ manifest  
                            var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
                            var gameName = nameMatch.Success ? nameMatch.Groups[1].Value : $"AppID {appId}";

                            // Bỏ qua Steamworks Common Redistributables và các gói phân phối lại
                            if (gameName.Contains("Steamworks Common Redistributables") ||
                                appId == "228980" ||
                                gameName.Contains("Redistributable") ||
                                gameName.Contains("Redist"))
                            {
                                continue;
                            }

                            // Lấy đường dẫn cài đặt (phụ huynh của steamapps)
                            var installDir = directory;

                            scannedGames.Add(new
                            {
                                appId,
                                gameName,
                                installDir
                            });
                        }
                    }
                }

                // Quét các thư mục con nếu chưa đạt độ sâu tối đa
                if (currentDepth < maxDepth)
                {
                    var subDirectories = Directory.GetDirectories(directory);
                    foreach (var subDir in subDirectories)
                    {
                        try
                        {
                            // Bỏ qua một số thư mục hệ thống để tăng tốc độ quét
                            var dirName = Path.GetFileName(subDir).ToLower();
                            if (dirName == "windows" || dirName == "program files" ||
                                dirName == "program files (x86)" || dirName == "$recycle.bin" ||
                                dirName == "system volume information" || dirName.StartsWith("$"))
                                continue;

                            await ScanDirectoryForGamesAsync(subDir, scannedGames, existingAppIds, processedPaths, maxDepth, currentDepth + 1);
                        }
                        catch
                        {
                            // Bỏ qua lỗi truy cập thư mục con
                        }
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi truy cập thư mục
            }
        }
    }
}