using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.IO;

namespace SteamCmdWeb.Services
{
    public class SyncService
    {
        private readonly ILogger<SyncService> _logger;
        private readonly ProfileService _profileService;
        private readonly HttpClient _httpClient;
        private readonly List<SyncResult> _syncResults = new List<SyncResult>();
        private readonly object _resultsLock = new object();
        private readonly string _dataFolder;
        private readonly string _clientsFilePath;

        private const int MAX_RESULTS = 100;
        private const int DEFAULT_PORT = 61188;
        private const int CONNECTION_TIMEOUT = 5000; // 5 giây

        public SyncService(
            ILogger<SyncService> logger,
            ProfileService profileService)
        {
            _logger = logger;
            _profileService = profileService;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Sync");
            if (!Directory.Exists(_dataFolder))
            {
                Directory.CreateDirectory(_dataFolder);
            }

            _clientsFilePath = Path.Combine(_dataFolder, "known_clients.json");
            if (!File.Exists(_clientsFilePath))
            {
                File.WriteAllText(_clientsFilePath, "[]");
            }
        }

        public List<SyncResult> GetSyncResults()
        {
            lock (_resultsLock)
            {
                return _syncResults.OrderByDescending(r => r.Timestamp).Take(30).ToList();
            }
        }

        private void AddSyncResult(SyncResult result)
        {
            lock (_resultsLock)
            {
                _syncResults.Insert(0, result);
                if (_syncResults.Count > MAX_RESULTS)
                {
                    _syncResults.RemoveRange(MAX_RESULTS, _syncResults.Count - MAX_RESULTS);
                }
            }
        }

        private async Task<List<ClientRegistration>> GetKnownClientsAsync()
        {
            try
            {
                string json = await File.ReadAllTextAsync(_clientsFilePath);
                return JsonSerializer.Deserialize<List<ClientRegistration>>(json) ?? new List<ClientRegistration>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc danh sách client");
                return new List<ClientRegistration>();
            }
        }

        private async Task SaveKnownClientsAsync(List<ClientRegistration> clients)
        {
            try
            {
                string json = JsonSerializer.Serialize(clients, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_clientsFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu danh sách client");
            }
        }

        public async Task<List<SyncResult>> SyncFromAllKnownClientsAsync()
        {
            var clients = await GetKnownClientsAsync();
            var results = new List<SyncResult>();

            foreach (var client in clients.Where(c => c.IsActive))
            {
                try
                {
                    var result = await SyncFromClientAsync(client);
                    results.Add(result);

                    // Cập nhật thông tin client
                    client.LastSyncAttempt = DateTime.Now;
                    if (result.Success)
                    {
                        client.LastSuccessfulSync = DateTime.Now;
                        client.LastSyncResults = $"Thành công. Thêm {result.NewProfilesAdded} profiles.";
                        client.ConnectionFailureCount = 0;
                    }
                    else
                    {
                        client.ConnectionFailureCount++;
                        client.LastSyncResults = result.Message;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi đồng bộ với client {ClientId}", client.ClientId);
                    client.ConnectionFailureCount++;
                    client.LastSyncResults = $"Lỗi: {ex.Message}";

                    results.Add(new SyncResult
                    {
                        ClientId = client.ClientId,
                        Success = false,
                        Message = $"Lỗi: {ex.Message}",
                        Timestamp = DateTime.Now
                    });
                }
            }

            await SaveKnownClientsAsync(clients);
            return results;
        }

        private async Task<SyncResult> SyncFromClientAsync(ClientRegistration client)
        {
            try
            {
                string url = $"http://{client.Address}:{client.Port}/api/profiles";

                _logger.LogInformation("Đang lấy profiles từ client {ClientId} tại {Url}", client.ClientId, url);

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return new SyncResult
                    {
                        ClientId = client.ClientId,
                        Success = false,
                        Message = $"Lỗi: HTTP {(int)response.StatusCode}",
                        Timestamp = DateTime.Now
                    };
                }

                var content = await response.Content.ReadAsStringAsync();
                var clientProfiles = JsonSerializer.Deserialize<List<ClientProfile>>(content);

                if (clientProfiles == null || !clientProfiles.Any())
                {
                    return new SyncResult
                    {
                        ClientId = client.ClientId,
                        Success = true,
                        TotalProfiles = 0,
                        Message = "Không có profiles để đồng bộ",
                        Timestamp = DateTime.Now
                    };
                }

                return await ProcessAndSaveProfilesAsync(client.ClientId, clientProfiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ với client {ClientId}", client.ClientId);
                return new SyncResult
                {
                    ClientId = client.ClientId,
                    Success = false,
                    Message = $"Lỗi: {ex.Message}",
                    Timestamp = DateTime.Now
                };
            }
        }

        public async Task<SyncResult> SyncFromIpAsync(string ip, int port = DEFAULT_PORT)
        {
            try
            {
                // Kiểm tra kết nối
                bool canConnect = await CheckConnectionAsync(ip, port);
                if (!canConnect)
                {
                    return new SyncResult
                    {
                        ClientId = $"{ip}:{port}",
                        Success = false,
                        Message = "Không thể kết nối đến client",
                        Timestamp = DateTime.Now
                    };
                }

                string url = $"http://{ip}:{port}/api/profiles";
                _logger.LogInformation("Đang lấy profiles từ IP {Ip}:{Port}", ip, port);

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return new SyncResult
                    {
                        ClientId = $"{ip}:{port}",
                        Success = false,
                        Message = $"Lỗi: HTTP {(int)response.StatusCode}",
                        Timestamp = DateTime.Now
                    };
                }

                var content = await response.Content.ReadAsStringAsync();
                var clientProfiles = JsonSerializer.Deserialize<List<ClientProfile>>(content);

                if (clientProfiles == null || !clientProfiles.Any())
                {
                    return new SyncResult
                    {
                        ClientId = $"{ip}:{port}",
                        Success = true,
                        TotalProfiles = 0,
                        Message = "Không có profiles để đồng bộ",
                        Timestamp = DateTime.Now
                    };
                }

                var clientId = $"{ip}:{port}";
                var result = await ProcessAndSaveProfilesAsync(clientId, clientProfiles);

                // Lưu hoặc cập nhật client vào danh sách đã biết
                await UpdateKnownClientAsync(ip, port, result.Success);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ IP {Ip}:{Port}", ip, port);
                return new SyncResult
                {
                    ClientId = $"{ip}:{port}",
                    Success = false,
                    Message = $"Lỗi: {ex.Message}",
                    Timestamp = DateTime.Now
                };
            }
        }

        public async Task<SyncResult> ProcessAndSaveProfilesAsync(string clientId, List<ClientProfile> clientProfiles)
        {
            try
            {
                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                int added = 0;
                int filtered = 0;

                _logger.LogInformation("Nhận được {Count} profiles từ client {ClientId}", clientProfiles.Count, clientId);

                foreach (var profile in clientProfiles)
                {
                    // Lọc profiles theo App ID
                    if (!existingAppIds.Contains(profile.AppID))
                    {
                        // Giữ nguyên tên đăng nhập và mật khẩu đã mã hóa
                        await _profileService.AddProfileAsync(profile);
                        existingAppIds.Add(profile.AppID);
                        added++;
                    }
                    else
                    {
                        filtered++;
                    }
                }

                var result = new SyncResult
                {
                    ClientId = clientId,
                    Success = true,
                    TotalProfiles = clientProfiles.Count,
                    NewProfilesAdded = added,
                    FilteredProfiles = filtered,
                    Message = $"Đồng bộ thành công. Thêm {added} profiles mới, bỏ qua {filtered} profiles trùng App ID.",
                    Timestamp = DateTime.Now
                };

                AddSyncResult(result);
                _logger.LogInformation("Đồng bộ từ {ClientId} hoàn tất: {Added} thêm mới, {Filtered} trùng lặp",
                    clientId, added, filtered);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý và lưu profiles từ {ClientId}", clientId);
                return new SyncResult
                {
                    ClientId = clientId,
                    Success = false,
                    Message = $"Lỗi: {ex.Message}",
                    Timestamp = DateTime.Now
                };
            }
        }

        private async Task UpdateKnownClientAsync(string ip, int port, bool syncSuccess)
        {
            var clients = await GetKnownClientsAsync();
            var clientId = $"{ip}:{port}";
            var existingClient = clients.FirstOrDefault(c => c.Address == ip && c.Port == port);

            if (existingClient != null)
            {
                existingClient.LastSyncAttempt = DateTime.Now;
                if (syncSuccess)
                {
                    existingClient.LastSuccessfulSync = DateTime.Now;
                    existingClient.ConnectionFailureCount = 0;
                }
                else
                {
                    existingClient.ConnectionFailureCount++;
                }
            }
            else
            {
                clients.Add(new ClientRegistration
                {
                    ClientId = clientId,
                    Address = ip,
                    Port = port,
                    Description = $"Tự động phát hiện vào {DateTime.Now}",
                    IsActive = true,
                    RegisteredAt = DateTime.Now,
                    LastSyncAttempt = DateTime.Now,
                    LastSuccessfulSync = syncSuccess ? DateTime.Now : DateTime.MinValue,
                    ConnectionFailureCount = syncSuccess ? 0 : 1
                });
            }

            await SaveKnownClientsAsync(clients);
        }

        private async Task<bool> CheckConnectionAsync(string host, int port, int timeout = CONNECTION_TIMEOUT)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    await Task.WhenAny(connectTask, Task.Delay(timeout));

                    if (client.Connected)
                    {
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Kết nối đến {Host}:{Port} quá hạn sau {Timeout}ms", host, port, timeout);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể kết nối đến {Host}:{Port}", host, port);
                return false;
            }
        }

        // Đổi tên ScanLocalNetworkAsync thành DiscoverAndSyncClientsAsync để phù hợp với yêu cầu
        public async Task DiscoverAndSyncClientsAsync()
        {
            try
            {
                _logger.LogInformation("Bắt đầu tìm kiếm và đồng bộ clients");

                // Lấy các địa chỉ IP local
                var localIPs = await GetLocalIPAddressesAsync();
                _logger.LogInformation("Tìm thấy {Count} IP local", localIPs.Count);

                var knownClients = await GetKnownClientsAsync();
                var discoveredClients = new List<(string ip, int port)>();

                // Thêm các client đã biết
                foreach (var client in knownClients)
                {
                    discoveredClients.Add((client.Address, client.Port));
                }

                _logger.LogInformation("Đã thêm {Count} clients đã biết", knownClients.Count);

                // Duyệt qua các địa chỉ IP và cố đồng bộ
                foreach (var clientInfo in discoveredClients.Distinct())
                {
                    try
                    {
                        var result = await SyncFromIpAsync(clientInfo.ip, clientInfo.port);
                        if (result.Success)
                        {
                            _logger.LogInformation("Đồng bộ thành công từ {Ip}:{Port}", clientInfo.ip, clientInfo.port);
                        }
                        else
                        {
                            _logger.LogWarning("Đồng bộ thất bại từ {Ip}:{Port}: {Message}",
                                clientInfo.ip, clientInfo.port, result.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi đồng bộ từ {Ip}:{Port}", clientInfo.ip, clientInfo.port);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tìm kiếm và đồng bộ clients");
                throw;
            }
        }

        private async Task<List<string>> GetLocalIPAddressesAsync()
        {
            var addresses = new List<string>();

            try
            {
                // Lấy hostname của máy local
                string hostName = Dns.GetHostName();
                IPHostEntry hostEntry = await Dns.GetHostEntryAsync(hostName);

                // Lọc các địa chỉ IPv4
                foreach (IPAddress ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(ip.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy địa chỉ IP local");
            }

            // Thêm địa chỉ loop-back
            if (!addresses.Contains("127.0.0.1"))
            {
                addresses.Add("127.0.0.1");
            }

            return addresses;
        }

        public async Task<List<ClientProfile>> GetAllProfilesAsync()
        {
            return await _profileService.GetAllProfilesAsync();
        }
    }
}