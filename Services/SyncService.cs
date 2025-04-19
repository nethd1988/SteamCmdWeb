using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
        private readonly List<ClientRegistration> _registeredClients = new List<ClientRegistration>();
        private readonly List<SyncResult> _syncResults = new List<SyncResult>();
        private readonly HttpClient _httpClient;
        private readonly object _clientsLock = new object();
        private readonly object _resultsLock = new object();

        public SyncService(ILogger<SyncService> logger, ProfileService profileService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public List<ClientRegistration> GetRegisteredClients()
        {
            lock (_clientsLock)
            {
                return new List<ClientRegistration>(_registeredClients);
            }
        }

        public void RegisterClient(ClientRegistration client)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            lock (_clientsLock)
            {
                _registeredClients.RemoveAll(c => c.ClientId == client.ClientId);
                _registeredClients.Add(client);
                _logger.LogInformation("Đã đăng ký client: {ClientId}, {Address}:{Port}", client.ClientId, client.Address, client.Port);
            }
        }

        public bool UnregisterClient(string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentNullException(nameof(clientId));

            lock (_clientsLock)
            {
                int count = _registeredClients.RemoveAll(c => c.ClientId == clientId);
                if (count > 0)
                {
                    _logger.LogInformation("Đã hủy đăng ký client: {ClientId}", clientId);
                    return true;
                }
                return false;
            }
        }

        public List<SyncResult> GetSyncResults()
        {
            lock (_resultsLock)
            {
                return new List<SyncResult>(_syncResults);
            }
        }

        private void AddSyncResult(SyncResult result)
        {
            if (result == null)
                return;

            lock (_resultsLock)
            {
                if (_syncResults.Count >= 100)
                {
                    _syncResults.RemoveAt(0);
                }
                _syncResults.Add(result);
            }
        }

        public async Task<SyncResult> SyncFromIpAsync(string ip, int port = 61188)
        {
            try
            {
                _logger.LogInformation("Bắt đầu đồng bộ từ {IP}:{Port}", ip, port);

                string url = $"http://{ip}:{port}/api/profiles";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    string errorMessage = $"Lỗi khi kết nối đến client: {response.StatusCode}";
                    _logger.LogError(errorMessage);
                    var result = new SyncResult
                    {
                        ClientId = $"{ip}:{port}",
                        Success = false,
                        Message = errorMessage,
                        Timestamp = DateTime.Now
                    };
                    AddSyncResult(result);
                    return result;
                }

                var content = await response.Content.ReadAsStringAsync();
                var clientProfiles = JsonSerializer.Deserialize<List<ClientProfile>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (clientProfiles == null || clientProfiles.Count == 0)
                {
                    var noProfilesResult = new SyncResult
                    {
                        ClientId = $"{ip}:{port}",
                        Success = true,
                        Message = "Không có profile nào để đồng bộ",
                        Timestamp = DateTime.Now,
                        TotalProfiles = 0
                    };
                    AddSyncResult(noProfilesResult);
                    return noProfilesResult;
                }

                return await ProcessSyncProfilesAsync(clientProfiles, $"{ip}:{port}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ {IP}:{Port}", ip, port);
                var result = new SyncResult
                {
                    ClientId = $"{ip}:{port}",
                    Success = false,
                    Message = $"Lỗi: {ex.Message}",
                    Timestamp = DateTime.Now
                };
                AddSyncResult(result);
                return result;
            }
        }

        public async Task<SyncResult> SyncProfilesFromClientAsync(ClientRegistration client)
        {
            try
            {
                _logger.LogInformation("Bắt đầu đồng bộ từ client {ClientId} ({Address}:{Port})",
                    client.ClientId, client.Address, client.Port);

                return await SyncFromIpAsync(client.Address, client.Port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId}", client.ClientId);
                var result = new SyncResult
                {
                    ClientId = client.ClientId,
                    Success = false,
                    Message = $"Lỗi: {ex.Message}",
                    Timestamp = DateTime.Now
                };
                AddSyncResult(result);
                return result;
            }
        }

        public async Task<List<SyncResult>> SyncFromAllKnownClientsAsync()
        {
            List<SyncResult> results = new List<SyncResult>();
            List<ClientRegistration> clients;

            lock (_clientsLock)
            {
                clients = new List<ClientRegistration>(_registeredClients);
            }

            foreach (var client in clients)
            {
                try
                {
                    var result = await SyncProfilesFromClientAsync(client);
                    results.Add(result);

                    lock (_clientsLock)
                    {
                        var clientToUpdate = _registeredClients.Find(c => c.ClientId == client.ClientId);
                        if (clientToUpdate != null)
                        {
                            clientToUpdate.LastSyncAttempt = DateTime.Now;
                            if (result.Success)
                            {
                                clientToUpdate.LastSuccessfulSync = DateTime.Now;
                                clientToUpdate.LastSyncResults = $"Thêm {result.NewProfilesAdded} profiles";
                                clientToUpdate.ConnectionFailureCount = 0;
                            }
                            else
                            {
                                clientToUpdate.ConnectionFailureCount++;
                                clientToUpdate.LastSyncResults = result.Message;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId}", client.ClientId);
                    var errorResult = new SyncResult
                    {
                        ClientId = client.ClientId,
                        Success = false,
                        Message = $"Lỗi: {ex.Message}",
                        Timestamp = DateTime.Now
                    };
                    results.Add(errorResult);
                    AddSyncResult(errorResult);
                }

                await Task.Delay(1000);
            }

            return results;
        }

        public async Task<List<SyncResult>> SyncFromAllClientsAsync()
        {
            return await SyncFromAllKnownClientsAsync();
        }

        private async Task<SyncResult> ProcessSyncProfilesAsync(List<ClientProfile> clientProfiles, string clientId)
        {
            try
            {
                _logger.LogInformation("Đang xử lý {Count} profiles từ client {ClientId}", clientProfiles.Count, clientId);

                int totalProfiles = clientProfiles.Count;
                int newProfilesAdded = 0;
                int filteredProfiles = 0;

                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = new HashSet<string>(existingProfiles.Select(p => p.AppID));

                var profilesToAdd = new List<ClientProfile>();

                foreach (var profile in clientProfiles)
                {
                    if (existingAppIds.Contains(profile.AppID))
                    {
                        filteredProfiles++;
                        continue;
                    }

                    profilesToAdd.Add(profile);
                }

                foreach (var profile in profilesToAdd)
                {
                    await _profileService.AddProfileAsync(profile);
                    newProfilesAdded++;
                }

                var result = new SyncResult
                {
                    ClientId = clientId,
                    Success = true,
                    Message = $"Đồng bộ thành công. Đã thêm {newProfilesAdded} profiles mới.",
                    TotalProfiles = totalProfiles,
                    NewProfilesAdded = newProfilesAdded,
                    FilteredProfiles = filteredProfiles,
                    Timestamp = DateTime.Now
                };

                AddSyncResult(result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý đồng bộ profiles từ client {ClientId}", clientId);
                var result = new SyncResult
                {
                    ClientId = clientId,
                    Success = false,
                    Message = $"Lỗi: {ex.Message}",
                    TotalProfiles = clientProfiles.Count,
                    Timestamp = DateTime.Now
                };
                AddSyncResult(result);
                return result;
            }
        }

        public async Task DiscoverAndSyncClientsAsync()
        {
            try
            {
                _logger.LogInformation("Bắt đầu khám phá và đồng bộ với client từ xa");

                await DiscoverFromCentralServiceAsync();
                await TryConnectToKnownServersAsync();
                await SyncFromRegisteredClientsAsync();

                _logger.LogInformation("Hoàn thành khám phá và đồng bộ client từ xa");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi khám phá và đồng bộ client từ xa");
            }
        }

        private async Task DiscoverFromCentralServiceAsync()
        {
            try
            {
                string discoveryServiceUrl = "https://discovery.steamcmdweb.example.com/api/clients";

                using var response = await _httpClient.GetAsync(discoveryServiceUrl);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Không thể kết nối với dịch vụ discovery trung tâm: {StatusCode}", response.StatusCode);
                    return;
                }

                var content = await response.Content.ReadAsStringAsync();
                var clients = JsonSerializer.Deserialize<List<ClientRegistration>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (clients == null || !clients.Any())
                {
                    _logger.LogInformation("Không tìm thấy client nào từ dịch vụ discovery trung tâm");
                    return;
                }

                _logger.LogInformation("Tìm thấy {Count} client từ dịch vụ discovery trung tâm", clients.Count);

                foreach (var client in clients)
                {
                    try
                    {
                        RegisterClient(client);
                        await SyncProfilesFromClientAsync(client);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi đồng bộ với client {ClientId} từ dịch vụ discovery", client.ClientId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tìm kiếm client từ dịch vụ discovery trung tâm");
            }
        }

        private async Task TryConnectToKnownServersAsync()
        {
            var knownServers = new List<(string Address, int Port)>
            {
                ("idckz.ddnsfree.com", 61188),
            };

            foreach (var server in knownServers)
            {
                try
                {
                    _logger.LogInformation("Đang thử kết nối với máy chủ đã biết: {Address}:{Port}", server.Address, server.Port);

                    bool isReachable = await IsHostReachableAsync(server.Address);
                    if (!isReachable)
                    {
                        _logger.LogWarning("Máy chủ {Address} không phản hồi", server.Address);
                        continue;
                    }

                    var result = await SyncFromIpAsync(server.Address, server.Port);
                    if (result.Success)
                    {
                        _logger.LogInformation("Đồng bộ thành công với máy chủ {Address}:{Port}", server.Address, server.Port);

                        RegisterClient(new ClientRegistration
                        {
                            ClientId = $"{server.Address}:{server.Port}",
                            Address = server.Address,
                            Port = server.Port,
                            Description = $"Máy chủ đã biết, kết nối thành công vào {DateTime.Now}",
                            RegisteredAt = DateTime.Now,
                            LastSuccessfulSync = DateTime.Now,
                            LastSyncAttempt = DateTime.Now,
                            LastSyncResults = $"Đã thêm {result.NewProfilesAdded} profiles",
                            IsActive = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi kết nối với máy chủ đã biết: {Address}:{Port}", server.Address, server.Port);
                }
            }
        }

        private async Task<bool> IsHostReachableAsync(string hostNameOrAddress)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(hostNameOrAddress, 3000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync(hostNameOrAddress, 61188);
                    if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                    {
                        return client.Connected;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        private async Task SyncFromRegisteredClientsAsync()
        {
            List<ClientRegistration> clients;

            lock (_clientsLock)
            {
                clients = new List<ClientRegistration>(_registeredClients);
            }

            _logger.LogInformation("Đồng bộ với {Count} client đã đăng ký trước đó", clients.Count);

            foreach (var client in clients)
            {
                try
                {
                    var result = await SyncProfilesFromClientAsync(client);

                    lock (_clientsLock)
                    {
                        var existingClient = _registeredClients.FirstOrDefault(c => c.ClientId == client.ClientId);
                        if (existingClient != null)
                        {
                            existingClient.LastSyncAttempt = DateTime.Now;
                            if (result.Success)
                            {
                                existingClient.LastSuccessfulSync = DateTime.Now;
                                existingClient.LastSyncResults = $"Thêm {result.NewProfilesAdded} profiles";
                                existingClient.ConnectionFailureCount = 0;
                            }
                            else
                            {
                                existingClient.ConnectionFailureCount++;
                                existingClient.LastSyncResults = result.Message;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi đồng bộ với client {ClientId}", client.ClientId);
                }
            }
        }

        public async Task ScanLocalNetworkAsync()
        {
            await DiscoverAndSyncClientsAsync();
        }

        private IEnumerable<string> GetLocalIpAddresses()
        {
            try
            {
                _logger.LogInformation("Bắt đầu quét mạng cục bộ để tìm client");
                var localIps = new List<string>();
                var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var address in hostEntry.AddressList)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        localIps.Add(address.ToString());
                    }
                }
                return localIps;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy địa chỉ IP cục bộ");
                return Enumerable.Empty<string>();
            }
        }
    }
}