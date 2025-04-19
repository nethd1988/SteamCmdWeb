using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
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
        private readonly List<SyncResult> _syncResults = new List<SyncResult>();
        private readonly object _syncLock = new object();
        private readonly List<ClientRegistration> _knownClients = new List<ClientRegistration>();
        private bool _isSyncing = false;

        private const string DEFAULT_SERVER = "idckz.ddnsfree.com";
        private const int DEFAULT_PORT = 61188;

        public SyncService(
            ILogger<SyncService> logger,
            ProfileService profileService,
            DecryptionService decryptionService)
        {
            _logger = logger;
            _profileService = profileService;
            _decryptionService = decryptionService;
            LoadKnownClients();
        }

        private void LoadKnownClients()
        {
            try
            {
                // Thêm client mặc định
                _knownClients.Add(new ClientRegistration
                {
                    ClientId = "default-client",
                    Description = "Client mặc định",
                    Address = DEFAULT_SERVER,
                    Port = DEFAULT_PORT,
                    IsActive = true,
                    RegisteredAt = DateTime.Now,
                    LastSuccessfulSync = DateTime.MinValue,
                    LastSyncAttempt = DateTime.MinValue
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải danh sách client đã biết");
            }
        }

        public List<SyncResult> GetSyncResults()
        {
            lock (_syncLock)
            {
                // Chỉ trả về 100 kết quả mới nhất để tránh quá tải bộ nhớ
                return _syncResults.OrderByDescending(r => r.Timestamp).Take(100).ToList();
            }
        }

        public async Task<List<SyncResult>> SyncFromAllKnownClientsAsync()
        {
            List<SyncResult> results = new List<SyncResult>();

            foreach (var client in _knownClients.Where(c => c.IsActive))
            {
                try
                {
                    var result = await SyncFromIpAsync(client.Address, client.Port);
                    result.ClientId = client.ClientId;
                    results.Add(result);
                    client.LastSyncAttempt = DateTime.Now;

                    if (result.Success)
                    {
                        client.LastSuccessfulSync = DateTime.Now;
                        client.ConnectionFailureCount = 0;
                    }
                    else
                    {
                        client.ConnectionFailureCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId}", client.ClientId);
                    results.Add(new SyncResult
                    {
                        ClientId = client.ClientId,
                        Success = false,
                        Message = ex.Message,
                        Timestamp = DateTime.Now
                    });
                    client.ConnectionFailureCount++;
                }
            }

            return results;
        }

        public async Task<SyncResult> SyncFromIpAsync(string ip, int port = 61188)
        {
            if (string.IsNullOrEmpty(ip))
            {
                throw new ArgumentException("Địa chỉ IP không được để trống", nameof(ip));
            }

            // Luôn sử dụng DEFAULT_SERVER cho đồng bộ
            if (ip != DEFAULT_SERVER)
            {
                _logger.LogWarning($"Sử dụng địa chỉ server mặc định {DEFAULT_SERVER} thay vì {ip}");
                ip = DEFAULT_SERVER;
            }

            var syncResult = new SyncResult
            {
                ClientId = ip,
                Timestamp = DateTime.Now,
                Success = false
            };

            try
            {
                _logger.LogInformation("Bắt đầu đồng bộ từ {IP}:{Port}", ip, port);

                using (var client = new TcpClient())
                {
                    // Đặt timeout để tránh chờ quá lâu
                    var connectTask = client.ConnectAsync(ip, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                    {
                        syncResult.Message = $"Kết nối đến {ip}:{port} bị timeout";
                        return syncResult;
                    }

                    if (!client.Connected)
                    {
                        syncResult.Message = $"Không thể kết nối đến {ip}:{port}";
                        return syncResult;
                    }

                    // Gửi yêu cầu lấy danh sách profile
                    await using var stream = client.GetStream();

                    // Gửi lệnh AUTH + GET_PROFILES_FULL
                    string command = $"AUTH:simple_auth_token GET_PROFILES_FULL";
                    byte[] commandBytes = Encoding.UTF8.GetBytes(command);
                    byte[] lengthBytes = BitConverter.GetBytes(commandBytes.Length);

                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(commandBytes, 0, commandBytes.Length);
                    await stream.FlushAsync();

                    // Đọc phản hồi
                    byte[] responseHeaderBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(responseHeaderBuffer, 0, 4);

                    if (bytesRead < 4)
                    {
                        syncResult.Message = "Không đọc được phản hồi từ server";
                        return syncResult;
                    }

                    int responseLength = BitConverter.ToInt32(responseHeaderBuffer, 0);
                    if (responseLength <= 0 || responseLength > 10 * 1024 * 1024) // Giới hạn 10MB
                    {
                        syncResult.Message = $"Độ dài phản hồi không hợp lệ: {responseLength}";
                        return syncResult;
                    }

                    byte[] responseBuffer = new byte[responseLength];
                    bytesRead = await stream.ReadAsync(responseBuffer, 0, responseLength);

                    if (bytesRead < responseLength)
                    {
                        syncResult.Message = "Phản hồi không đầy đủ từ server";
                        return syncResult;
                    }

                    string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                    if (response == "NO_PROFILES")
                    {
                        syncResult.Success = true;
                        syncResult.Message = "Server báo không có profiles";
                        return syncResult;
                    }

                    // Chuyển đổi JSON sang danh sách profiles
                    var clientProfiles = JsonSerializer.Deserialize<List<SteamCmdWebAPI.Models.SteamCmdProfile>>(response);

                    if (clientProfiles == null || !clientProfiles.Any())
                    {
                        syncResult.Success = true;
                        syncResult.Message = "Không có profiles từ client";
                        return syncResult;
                    }

                    syncResult.TotalProfiles = clientProfiles.Count;

                    // Xử lý đồng bộ
                    var existingProfiles = await _profileService.GetAllProfilesAsync();
                    var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                    int added = 0;
                    int filtered = 0;

                    foreach (var clientProfile in clientProfiles)
                    {
                        // Chỉ lấy các profile có AppID chưa tồn tại
                        if (!existingAppIds.Contains(clientProfile.AppID))
                        {
                            // Chuyển đổi từ SteamCmdProfile sang ClientProfile
                            var newProfile = new ClientProfile
                            {
                                Name = clientProfile.Name,
                                AppID = clientProfile.AppID,
                                InstallDirectory = clientProfile.InstallDirectory,
                                SteamUsername = clientProfile.SteamUsername,
                                SteamPassword = clientProfile.SteamPassword,
                                Arguments = clientProfile.Arguments,
                                ValidateFiles = clientProfile.ValidateFiles,
                                AutoRun = clientProfile.AutoRun,
                                AnonymousLogin = clientProfile.AnonymousLogin,
                                Status = "Ready",
                                StartTime = DateTime.Now,
                                StopTime = DateTime.Now,
                                LastRun = DateTime.UtcNow
                            };

                            await _profileService.AddProfileAsync(newProfile);
                            existingAppIds.Add(clientProfile.AppID); // Cập nhật để không thêm trùng
                            added++;
                        }
                        else
                        {
                            filtered++;
                        }
                    }

                    syncResult.Success = true;
                    syncResult.NewProfilesAdded = added;
                    syncResult.FilteredProfiles = filtered;
                    syncResult.Message = $"Đồng bộ thành công. Đã thêm {added} profiles mới, bỏ qua {filtered} profiles đã tồn tại.";

                    _logger.LogInformation("Đồng bộ thành công từ {IP}:{Port}. Thêm: {Added}, Bỏ qua: {Filtered}",
                        ip, port, added, filtered);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ {IP}:{Port}", ip, port);
                syncResult.Message = $"Lỗi: {ex.Message}";
            }

            // Lưu kết quả đồng bộ
            lock (_syncLock)
            {
                _syncResults.Add(syncResult);
                if (_syncResults.Count > 1000) // Giới hạn số lượng kết quả lưu trữ
                {
                    _syncResults.RemoveAt(0);
                }
            }

            return syncResult;
        }

        public async Task DiscoverAndSyncClientsAsync()
        {
            _logger.LogInformation("Bắt đầu tìm kiếm client SteamCmdWebAPI trên mạng");

            // Sử dụng địa chỉ mặc định
            await SyncFromIpAsync(DEFAULT_SERVER, DEFAULT_PORT);
        }

        public async Task ScanLocalNetworkAsync()
        {
            await DiscoverAndSyncClientsAsync(); // Đơn giản hóa bằng cách gọi đến phương thức khám phá
        }
    }
}