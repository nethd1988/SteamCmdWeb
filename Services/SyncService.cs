using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class SyncService
    {
        private readonly ILogger<SyncService> _logger;
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;
        private readonly string _syncResultsPath;
        private readonly HttpClient _httpClient;
        private readonly List<SyncResult> _syncResults = new List<SyncResult>();

        public SyncService(
            ILogger<SyncService> logger,
            ProfileService profileService,
            DecryptionService decryptionService)
        {
            _logger = logger;
            _profileService = profileService;
            _decryptionService = decryptionService;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(2);

            var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            if (!Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
            }

            var syncFolder = Path.Combine(dataFolder, "Sync");
            if (!Directory.Exists(syncFolder))
            {
                Directory.CreateDirectory(syncFolder);
            }

            _syncResultsPath = Path.Combine(syncFolder, "results.json");

            LoadSyncResults();
        }

        private void LoadSyncResults()
        {
            try
            {
                if (File.Exists(_syncResultsPath))
                {
                    string json = File.ReadAllText(_syncResultsPath);
                    _syncResults = JsonSerializer.Deserialize<List<SyncResult>>(json) ?? new List<SyncResult>();
                    _logger.LogInformation("Đã tải {Count} kết quả đồng bộ", _syncResults.Count);
                }
                else
                {
                    _syncResults = new List<SyncResult>();
                    File.WriteAllText(_syncResultsPath, JsonSerializer.Serialize(_syncResults));
                    _logger.LogInformation("Đã tạo file results.json mới");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải kết quả đồng bộ");
                _syncResults = new List<SyncResult>();
            }
        }

        private void SaveSyncResult(SyncResult result)
        {
            try
            {
                _syncResults.Add(result);

                // Giới hạn số lượng kết quả lưu trữ (chỉ giữ 100 kết quả gần nhất)
                if (_syncResults.Count > 100)
                {
                    _syncResults = _syncResults.OrderByDescending(r => r.Timestamp).Take(100).ToList();
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_syncResults, options);
                File.WriteAllText(_syncResultsPath, json);
                _logger.LogInformation("Đã lưu kết quả đồng bộ từ client {ClientId}", result.ClientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu kết quả đồng bộ từ client {ClientId}", result.ClientId);
            }
        }

        public List<SyncResult> GetSyncResults()
        {
            return _syncResults.OrderByDescending(r => r.Timestamp).Take(20).ToList();
        }

        public async Task ScanLocalNetworkAsync()
        {
            try
            {
                _logger.LogInformation("Bắt đầu quét mạng cục bộ để tìm client");
                var localIps = GetLocalIpAddresses();

                foreach (var ip in localIps)
                {
                    _logger.LogInformation("Quét mạng dựa trên IP local: {IpAddress}", ip);
                    // Lấy phần subnet từ IP để quét
                    var parts = ip.Split('.');
                    if (parts.Length == 4)
                    {
                        var subnet = $"{parts[0]}.{parts[1]}.{parts[2]}";
                        await ScanSubnetAsync(subnet);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi quét mạng cục bộ");
            }
        }

        private List<string> GetLocalIpAddresses()
        {
            var hostName = Dns.GetHostName();
            var addresses = Dns.GetHostAddresses(hostName);
            return addresses
                .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();
        }

        private async Task ScanSubnetAsync(string subnet)
        {
            const int port = 61188;
            var tasks = new List<Task>();

            for (int i = 1; i < 255; i++)
            {
                string ip = $"{subnet}.{i}";
                tasks.Add(CheckIpForClientAsync(ip, port));

                // Giới hạn số lượng task đồng thời để tránh quá tải
                if (tasks.Count >= 20)
                {
                    await Task.WhenAny(tasks);
                    tasks = tasks.Where(t => !t.IsCompleted).ToList();
                }
            }

            // Đợi tất cả task còn lại hoàn thành
            await Task.WhenAll(tasks);
        }

        private async Task CheckIpForClientAsync(string ip, int port)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                if (await Task.WhenAny(connectTask, Task.Delay(300)) == connectTask)
                {
                    // Nếu kết nối thành công, thử lấy profiles
                    await SyncFromIpAsync(ip, port);
                }
            }
            catch
            {
                // Bỏ qua các lỗi kết nối
            }
        }

        public async Task<SyncResult> SyncFromIpAsync(string address, int port = 61188)
        {
            var clientId = $"{address}:{port}";
            var result = new SyncResult
            {
                ClientId = clientId,
                Timestamp = DateTime.Now
            };

            try
            {
                _logger.LogInformation("Bắt đầu đồng bộ từ client tại {Address}:{Port}", address, port);

                // Kiểm tra và lấy profiles từ client
                string apiUrl = $"http://{address}:{port}/api/profiles";
                _logger.LogInformation("Gọi API: {Url}", apiUrl);

                // Gọi API để lấy profiles từ client
                var response = await _httpClient.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    result.Success = false;
                    result.Message = $"Lỗi từ API client: {response.StatusCode}";
                    SaveSyncResult(result);

                    _logger.LogWarning("Lỗi khi gọi API client {ClientId}: {StatusCode}", clientId, response.StatusCode);
                    return result;
                }

                var content = await response.Content.ReadAsStringAsync();
                var clientProfiles = JsonSerializer.Deserialize<List<ClientProfile>>(content);

                if (clientProfiles == null || clientProfiles.Count == 0)
                {
                    result.Success = true;
                    result.Message = "Không có profiles trên client";
                    result.TotalProfiles = 0;
                    SaveSyncResult(result);

                    _logger.LogInformation("Không có profiles trên client {ClientId}", clientId);
                    return result;
                }

                // Lấy tất cả AppID có trên server
                var serverProfiles = await _profileService.GetAllProfilesAsync();
                var serverAppIds = serverProfiles.Select(p => p.AppID.ToLower()).ToHashSet();

                // Lọc chỉ lấy các profile có AppID chưa có trên server
                var newProfiles = clientProfiles
                    .Where(p => !serverAppIds.Contains(p.AppID.ToLower()))
                    .ToList();

                _logger.LogInformation("Tìm thấy {TotalCount} profiles trên client, có {NewCount} profiles mới",
                    clientProfiles.Count, newProfiles.Count);

                // Thêm các profile mới vào server
                int addedCount = 0;
                foreach (var profile in newProfiles)
                {
                    try
                    {
                        // Tạo profile mới với trạng thái mặc định
                        var newProfile = new ClientProfile
                        {
                            Name = profile.Name,
                            AppID = profile.AppID,
                            InstallDirectory = profile.InstallDirectory,
                            SteamUsername = profile.SteamUsername,
                            SteamPassword = profile.SteamPassword,
                            Arguments = profile.Arguments,
                            ValidateFiles = profile.ValidateFiles,
                            AutoRun = profile.AutoRun,
                            AnonymousLogin = profile.AnonymousLogin,
                            Status = "Ready",
                            StartTime = DateTime.Now,
                            StopTime = DateTime.Now,
                            LastRun = DateTime.Now,
                            Pid = 0
                        };

                        await _profileService.AddProfileAsync(newProfile);
                        addedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi thêm profile {ProfileName} từ client {ClientId}", profile.Name, clientId);
                    }
                }

                // Cập nhật kết quả
                result.Success = true;
                result.TotalProfiles = clientProfiles.Count;
                result.NewProfilesAdded = addedCount;
                result.FilteredProfiles = clientProfiles.Count - newProfiles.Count;
                result.Message = $"Đã thêm {addedCount} profiles mới từ {clientProfiles.Count} profiles trên client";
                SaveSyncResult(result);

                _logger.LogInformation("Đồng bộ thành công từ client {ClientId}: {Message}", clientId, result.Message);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId}", clientId);

                result.Success = false;
                result.Message = $"Lỗi: {ex.Message}";
                SaveSyncResult(result);

                return result;
            }
        }

        public async Task<List<SyncResult>> SyncFromAllKnownClientsAsync()
        {
            var results = new List<SyncResult>();
            var scannedIps = new HashSet<string>();

            // Quét các IP có kết quả đồng bộ thành công gần đây
            var recentSuccessfulClients = _syncResults
                .Where(r => r.Success && (DateTime.Now - r.Timestamp).TotalHours <= 24)
                .Select(r => r.ClientId.Split(':')[0])
                .Distinct()
                .ToList();

            foreach (var ip in recentSuccessfulClients)
            {
                if (scannedIps.Add(ip)) // Thêm vào danh sách đã quét
                {
                    try
                    {
                        var result = await SyncFromIpAsync(ip);
                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi đồng bộ từ client đã biết {ClientIp}", ip);
                    }
                }
            }

            return results;
        }
    }
}