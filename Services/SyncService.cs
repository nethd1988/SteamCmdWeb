using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.IO;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class SyncService
    {
        private readonly ILogger<SyncService> _logger;
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;
        private readonly HttpClient _httpClient;

        // Danh sách các client đăng ký - sẽ lưu trữ vào đĩa
        private List<ClientRegistration> _registeredClients = new List<ClientRegistration>();
        private readonly string _clientRegistrationPath;

        // Danh sách lưu trữ kết quả đồng bộ gần đây
        private List<SyncResult> _recentSyncResults = new List<SyncResult>();
        private readonly int _maxSyncResultsToKeep = 100;

        public SyncService(
            ILogger<SyncService> logger,
            ProfileService profileService,
            DecryptionService decryptionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 phút timeout

            // Tạo đường dẫn lưu trữ danh sách client
            var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _clientRegistrationPath = Path.Combine(dataDir, "clients.json");

            // Tải danh sách client đã đăng ký
            LoadRegisteredClients();
        }

        private void LoadRegisteredClients()
        {
            try
            {
                if (File.Exists(_clientRegistrationPath))
                {
                    string json = File.ReadAllText(_clientRegistrationPath);
                    var clients = JsonSerializer.Deserialize<List<ClientRegistration>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (clients != null)
                    {
                        _registeredClients = clients;
                        _logger.LogInformation("Đã tải {Count} clients đã đăng ký", _registeredClients.Count);
                    }
                }
                else
                {
                    _logger.LogInformation("Không tìm thấy file clients.json, khởi tạo danh sách mới");
                    _registeredClients = new List<ClientRegistration>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải danh sách clients đã đăng ký");
                _registeredClients = new List<ClientRegistration>();
            }
        }

        private void SaveRegisteredClients()
        {
            try
            {
                string json = JsonSerializer.Serialize(_registeredClients,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_clientRegistrationPath, json);
                _logger.LogInformation("Đã lưu {Count} clients đã đăng ký", _registeredClients.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu danh sách clients đã đăng ký");
            }
        }

        // Đăng ký client mới
        public void RegisterClient(ClientRegistration client)
        {
            lock (_registeredClients)
            {
                // Tìm client hiện có với cùng ID
                var existingClient = _registeredClients.FirstOrDefault(c => c.ClientId == client.ClientId);
                if (existingClient != null)
                {
                    _registeredClients.Remove(existingClient);
                }

                // Thêm client mới
                _registeredClients.Add(client);
                _logger.LogInformation("Đã đăng ký client {ClientId} tại {Address}:{Port}",
                    client.ClientId, client.Address, client.Port);

                // Lưu danh sách vào đĩa
                SaveRegisteredClients();
            }
        }

        // Lấy danh sách client đã đăng ký
        public List<ClientRegistration> GetRegisteredClients()
        {
            lock (_registeredClients)
            {
                return _registeredClients.ToList();
            }
        }

        // Lấy danh sách kết quả đồng bộ gần đây
        public List<SyncResult> GetRecentSyncResults()
        {
            lock (_recentSyncResults)
            {
                return _recentSyncResults.OrderByDescending(r => r.Timestamp).ToList();
            }
        }

        // Xóa đăng ký client
        public bool UnregisterClient(string clientId)
        {
            lock (_registeredClients)
            {
                var client = _registeredClients.FirstOrDefault(c => c.ClientId == clientId);
                if (client != null)
                {
                    _registeredClients.Remove(client);
                    _logger.LogInformation("Đã xóa đăng ký client {ClientId}", clientId);

                    // Lưu danh sách vào đĩa
                    SaveRegisteredClients();
                    return true;
                }
                return false;
            }
        }

        // Cập nhật địa chỉ cho client
        public bool UpdateClientAddress(string clientId, string newAddress, int port = 61188)
        {
            lock (_registeredClients)
            {
                var client = _registeredClients.FirstOrDefault(c => c.ClientId == clientId);
                if (client != null)
                {
                    string oldAddress = client.Address;
                    client.Address = newAddress;

                    if (port > 0)
                    {
                        client.Port = port;
                    }

                    client.IsActive = true;
                    client.ConnectionFailureCount = 0;

                    _logger.LogInformation("Client {ClientId} đã cập nhật địa chỉ từ {OldAddress} sang {NewAddress}",
                        clientId, oldAddress, newAddress);

                    // Lưu danh sách vào đĩa
                    SaveRegisteredClients();
                    return true;
                }
                return false;
            }
        }

        // Đồng bộ profile từ client
        public async Task<SyncResult> SyncProfilesFromClientAsync(ClientRegistration client)
        {
            SyncResult result = new SyncResult
            {
                ClientId = client.ClientId,
                Timestamp = DateTime.Now
            };

            try
            {
                _logger.LogInformation("Bắt đầu đồng bộ từ client {ClientId} tại {Address}:{Port}",
                    client.ClientId, client.Address, client.Port);

                string url = $"http://{client.Address}:{client.Port}/api/sync/profiles";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(client.AuthToken))
                {
                    request.Headers.Add("Authorization", $"Bearer {client.AuthToken}");
                }

                // Thiết lập timeout ngắn hơn cho việc kiểm tra kết nối
                _httpClient.Timeout = TimeSpan.FromSeconds(10);
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Không thể kết nối đến client {ClientId}. Status: {StatusCode}",
                        client.ClientId, response.StatusCode);

                    result.Success = false;
                    result.Message = $"Không thể kết nối đến client. Status: {response.StatusCode}";

                    // Cập nhật trạng thái client
                    lock (_registeredClients)
                    {
                        var regClient = _registeredClients.FirstOrDefault(c => c.ClientId == client.ClientId);
                        if (regClient != null)
                        {
                            regClient.ConnectionFailureCount++;
                            regClient.LastSyncAttempt = DateTime.Now;
                            regClient.LastSyncResults = result.Message;

                            // Nếu quá nhiều lần thất bại, vô hiệu hóa client
                            if (regClient.ConnectionFailureCount > 5)
                            {
                                regClient.IsActive = false;
                                _logger.LogWarning("Client {ClientId} đã bị vô hiệu hóa sau {Count} lần thất bại",
                                    client.ClientId, regClient.ConnectionFailureCount);
                            }

                            SaveRegisteredClients();
                        }
                    }

                    AddSyncResult(result);
                    return result;
                }

                var content = await response.Content.ReadAsStringAsync();
                var clientProfiles = JsonSerializer.Deserialize<List<ClientProfile>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (clientProfiles == null || clientProfiles.Count == 0)
                {
                    _logger.LogInformation("Không có profiles từ client {ClientId}", client.ClientId);

                    result.Success = true;
                    result.Message = "Không có profiles để đồng bộ";
                    result.TotalProfiles = 0;

                    // Cập nhật trạng thái client
                    lock (_registeredClients)
                    {
                        var regClient = _registeredClients.FirstOrDefault(c => c.ClientId == client.ClientId);
                        if (regClient != null)
                        {
                            regClient.LastSuccessfulSync = DateTime.Now;
                            regClient.LastSyncAttempt = DateTime.Now;
                            regClient.ConnectionFailureCount = 0;
                            regClient.LastSyncResults = result.Message;
                            regClient.IsActive = true;
                            SaveRegisteredClients();
                        }
                    }

                    AddSyncResult(result);
                    return result;
                }

                // Lấy danh sách các profile hiện có trên server
                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                // Lọc các profile có AppID chưa tồn tại trên server
                var newProfiles = clientProfiles.Where(p => !existingAppIds.Contains(p.AppID)).ToList();

                int addedCount = 0;
                foreach (var profile in newProfiles)
                {
                    try
                    {
                        // Bộ sung cài đặt mặc định trước khi thêm
                        profile.Status = "Ready";
                        profile.StartTime = DateTime.Now;
                        profile.StopTime = DateTime.Now;
                        profile.LastRun = DateTime.UtcNow;
                        profile.Id = 0; // Để hệ thống tự tạo ID mới

                        // Thêm profile mới
                        await _profileService.AddProfileAsync(profile);
                        addedCount++;

                        _logger.LogInformation("Đã thêm profile {Name} (AppID: {AppID}) từ client {ClientId}",
                            profile.Name, profile.AppID, client.ClientId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi thêm profile {Name} từ client {ClientId}",
                            profile.Name, client.ClientId);
                    }
                }

                result.Success = true;
                result.Message = $"Đồng bộ thành công từ client {client.ClientId}";
                result.TotalProfiles = clientProfiles.Count;
                result.NewProfilesAdded = addedCount;
                result.FilteredProfiles = clientProfiles.Count - newProfiles.Count;

                // Cập nhật trạng thái client
                lock (_registeredClients)
                {
                    var regClient = _registeredClients.FirstOrDefault(c => c.ClientId == client.ClientId);
                    if (regClient != null)
                    {
                        regClient.LastSuccessfulSync = DateTime.Now;
                        regClient.LastSyncAttempt = DateTime.Now;
                        regClient.ConnectionFailureCount = 0;
                        regClient.LastSyncResults = result.Message;
                        regClient.IsActive = true;
                        SaveRegisteredClients();
                    }
                }

                AddSyncResult(result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId}", client.ClientId);

                result.Success = false;
                result.Message = $"Lỗi khi đồng bộ: {ex.Message}";

                // Cập nhật trạng thái client
                lock (_registeredClients)
                {
                    var regClient = _registeredClients.FirstOrDefault(c => c.ClientId == client.ClientId);
                    if (regClient != null)
                    {
                        regClient.ConnectionFailureCount++;
                        regClient.LastSyncAttempt = DateTime.Now;
                        regClient.LastSyncResults = result.Message;
                        SaveRegisteredClients();
                    }
                }

                AddSyncResult(result);
                return result;
            }
        }

        // Thêm kết quả đồng bộ vào danh sách
        private void AddSyncResult(SyncResult result)
        {
            lock (_recentSyncResults)
            {
                _recentSyncResults.Add(result);

                // Giới hạn số lượng kết quả lưu trữ
                if (_recentSyncResults.Count > _maxSyncResultsToKeep)
                {
                    _recentSyncResults = _recentSyncResults
                        .OrderByDescending(r => r.Timestamp)
                        .Take(_maxSyncResultsToKeep)
                        .ToList();
                }
            }
        }

        // Đồng bộ từ tất cả client đã đăng ký
        public async Task<List<SyncResult>> SyncFromAllClientsAsync()
        {
            _logger.LogInformation("Bắt đầu đồng bộ từ tất cả client đã đăng ký ({Count} clients)",
                _registeredClients.Count);

            var results = new List<SyncResult>();
            var activeClients = GetRegisteredClients().Where(c => c.IsActive).ToList();

            if (activeClients.Count == 0)
            {
                _logger.LogInformation("Không có client nào được kích hoạt để đồng bộ");
            }

            foreach (var client in activeClients)
            {
                try
                {
                    var result = await SyncProfilesFromClientAsync(client);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId}", client.ClientId);
                    results.Add(new SyncResult
                    {
                        ClientId = client.ClientId,
                        Success = false,
                        Message = $"Exception: {ex.Message}",
                        Timestamp = DateTime.Now
                    });
                }
            }

            return results;
        }

        // Tự động đồng bộ silent
        public async Task AutoSyncAsync()
        {
            _logger.LogInformation("Bắt đầu tự động đồng bộ từ các client đã đăng ký");
            try
            {
                var results = await SyncFromAllClientsAsync();

                int successCount = results.Count(r => r.Success);
                int totalNewProfiles = results.Sum(r => r.NewProfilesAdded);

                _logger.LogInformation("Hoàn thành tự động đồng bộ: {SuccessCount}/{TotalCount} thành công, {NewProfilesCount} profiles mới",
                    successCount, results.Count, totalNewProfiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động đồng bộ");
            }
        }

        // Tự động phát hiện và đăng ký client
        public async Task AutoDiscoverAndRegisterClientsAsync()
        {
            try
            {
                _logger.LogInformation("Bắt đầu tự động phát hiện clients...");

                // Quét các client đã có nhưng có thể đã thay đổi địa chỉ
                await UpdateClientAddressesAsync();

                // Quét mạng để tìm client mới
                await ScanNetworkForClientsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tự động phát hiện clients");
            }
        }

        private async Task UpdateClientAddressesAsync()
        {
            _logger.LogInformation("Kiểm tra và cập nhật địa chỉ của clients");

            var clients = GetRegisteredClients();
            foreach (var client in clients)
            {
                if (!client.IsActive)
                {
                    continue; // Bỏ qua các client không kích hoạt
                }

                try
                {
                    // Kiểm tra xem client có hoạt động không với địa chỉ hiện tại
                    bool isReachable = await CheckClientReachabilityAsync(client.Address, client.Port);

                    if (!isReachable)
                    {
                        _logger.LogWarning("Client {ClientId} không thể kết nối tại {Address}:{Port}",
                            client.ClientId, client.Address, client.Port);

                        // Không làm gì thêm, để hệ thống đánh dấu thất bại trong lần sync tiếp theo
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi kiểm tra client {ClientId} tại {Address}:{Port}",
                        client.ClientId, client.Address, client.Port);
                }
            }
        }

        private async Task<bool> CheckClientReachabilityAsync(string address, int port)
        {
            try
            {
                using (var tcpClient = new TcpClient())
                {
                    var connectTask = tcpClient.ConnectAsync(address, port);

                    // Đặt timeout 2 giây
                    if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask)
                    {
                        return tcpClient.Connected;
                    }
                    return false; // Timeout
                }
            }
            catch
            {
                return false; // Không thể kết nối
            }
        }

        private async Task ScanNetworkForClientsAsync()
        {
            _logger.LogInformation("Bắt đầu quét mạng để tìm kiếm client mới");

            // Lấy danh sách các client đã đăng ký
            var registeredClientIds = _registeredClients.Select(c => c.ClientId).ToHashSet();

            // Tạo danh sách các IP cần quét (có thể tùy chỉnh theo mạng của bạn)
            var baseIp = "192.168.1.";
            var portToScan = 61188; // Port mặc định của client

            var scanTasks = new List<Task>();

            // Quét từ 2 đến 254 (bỏ qua 0, 1 thường dùng cho gateway)
            for (int i = 2; i <= 254; i++)
            {
                var ip = baseIp + i;
                scanTasks.Add(ScanIpForClientAsync(ip, portToScan, registeredClientIds));
            }

            // Chờ tất cả các task hoàn thành
            await Task.WhenAll(scanTasks);

            _logger.LogInformation("Hoàn thành quét mạng tìm kiếm client");
        }

        private async Task ScanIpForClientAsync(string ipAddress, int port, HashSet<string> existingClientIds)
        {
            try
            {
                // Kiểm tra xem IP có phản hồi không
                bool isReachable = await CheckClientReachabilityAsync(ipAddress, port);

                if (isReachable)
                {
                    _logger.LogInformation("Phát hiện máy chủ tại {Address}:{Port}, đang kiểm tra...", ipAddress, port);

                    // Thử lấy thông tin client
                    try
                    {
                        string url = $"http://{ipAddress}:{port}/api/sync/info";

                        // Đặt timeout ngắn
                        _httpClient.Timeout = TimeSpan.FromSeconds(5);
                        var response = await _httpClient.GetAsync(url);

                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            var clientInfo = JsonSerializer.Deserialize<ClientInfoResponse>(content,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (clientInfo != null && !string.IsNullOrEmpty(clientInfo.ClientId))
                            {
                                // Kiểm tra xem client đã được đăng ký chưa
                                if (!existingClientIds.Contains(clientInfo.ClientId))
                                {
                                    var newClient = new ClientRegistration
                                    {
                                        ClientId = clientInfo.ClientId,
                                        Description = clientInfo.Description ?? $"Tự động phát hiện ({clientInfo.ClientId})",
                                        Address = ipAddress,
                                        Port = port,
                                        AuthToken = clientInfo.AuthToken ?? Guid.NewGuid().ToString("N"),
                                        IsActive = true,
                                        RegisteredAt = DateTime.Now
                                    };

                                    RegisterClient(newClient);

                                    _logger.LogInformation("Đã tự động đăng ký client mới: {ClientId} tại {Address}:{Port}",
                                        clientInfo.ClientId, ipAddress, port);
                                }
                                else
                                {
                                    // Cập nhật địa chỉ cho client đã có
                                    UpdateClientAddress(clientInfo.ClientId, ipAddress, port);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Không thể lấy thông tin client tại {Address}:{Port}", ipAddress, port);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Lỗi khi quét {Address}:{Port}", ipAddress, port);
            }
        }

        private class ClientInfoResponse
        {
            public string ClientId { get; set; }
            public string Description { get; set; }
            public string AuthToken { get; set; }
        }
    }
}