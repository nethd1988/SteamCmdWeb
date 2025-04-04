using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    /// <summary>
    /// Dịch vụ chạy ngầm để quản lý việc đồng bộ với client
    /// </summary>
    public class ClientSyncService : BackgroundService
    {
        private readonly ILogger<ClientSyncService> _logger;
        private readonly AppProfileManager _profileManager;
        private readonly string _syncFolder;
        
        // Danh sách các client đã đăng ký
        private readonly Dictionary<string, ClientRegistration> _registeredClients = new Dictionary<string, ClientRegistration>();
        
        // Thời gian chờ giữa các lần quét
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(30);
        
        // Semaphore để giới hạn số lượng đồng bộ đồng thời
        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(5, 5);

        public ClientSyncService(ILogger<ClientSyncService> logger, AppProfileManager profileManager)
        {
            _logger = logger;
            _profileManager = profileManager;
            
            _syncFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "ClientSync");
            if (!Directory.Exists(_syncFolder))
            {
                Directory.CreateDirectory(_syncFolder);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Client Sync Service started");
                
                // Tải danh sách client đã đăng ký từ tệp cấu hình nếu có
                await LoadRegisteredClientsAsync();

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await SyncWithClientsAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during client sync");
                    }

                    await Task.Delay(_syncInterval, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in Client Sync Service");
            }
        }

        /// <summary>
        /// Đồng bộ dữ liệu với tất cả các client đã đăng ký
        /// </summary>
        private async Task SyncWithClientsAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting sync with registered clients. Client count: {Count}", _registeredClients.Count);
            
            var syncTasks = new List<Task>();
            var clientsToSync = _registeredClients.Values.ToList();
            
            foreach (var client in clientsToSync)
            {
                if (stoppingToken.IsCancellationRequested) break;
                
                // Kiểm tra xem client có cần đồng bộ không
                if (!ShouldSyncWithClient(client)) continue;
                
                // Đợi semaphore
                await _syncSemaphore.WaitAsync(stoppingToken);
                
                // Tạo task đồng bộ với client
                var syncTask = Task.Run(async () => {
                    try
                    {
                        await SyncWithClientAsync(client, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error syncing with client {ClientId}", client.ClientId);
                    }
                    finally
                    {
                        _syncSemaphore.Release();
                    }
                }, stoppingToken);
                
                syncTasks.Add(syncTask);
            }
            
            // Đợi tất cả các task hoàn thành
            if (syncTasks.Any())
            {
                await Task.WhenAll(syncTasks);
                _logger.LogInformation("Completed sync with {Count} clients", syncTasks.Count);
            }
            else
            {
                _logger.LogInformation("No clients needed syncing at this time");
            }
        }

        /// <summary>
        /// Kiểm tra xem client có cần đồng bộ không
        /// </summary>
        private bool ShouldSyncWithClient(ClientRegistration client)
        {
            // Nếu client không hoạt động, bỏ qua
            if (!client.IsActive) return false;
            
            // Nếu đã đồng bộ trong vòng khoảng thời gian quy định, bỏ qua
            if ((DateTime.UtcNow - client.LastSyncAttempt).TotalMinutes < client.SyncIntervalMinutes)
                return false;
            
            return true;
        }

        /// <summary>
        /// Đồng bộ dữ liệu với một client cụ thể
        /// </summary>
        private async Task SyncWithClientAsync(ClientRegistration client, CancellationToken stoppingToken)
        {
            _logger.LogInformation("Syncing with client {ClientId} at {Address}:{Port}", 
                client.ClientId, client.Address, client.Port);
            
            // Cập nhật thời gian đồng bộ cuối
            client.LastSyncAttempt = DateTime.UtcNow;
            
            TcpClient tcpClient = null;
            
            try
            {
                // Kết nối đến client
                tcpClient = new TcpClient();
                
                // Thiết lập timeout
                var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, connectCts.Token);
                
                try
                {
                    await tcpClient.ConnectAsync(client.Address, client.Port, linkedCts.Token);
                }
                catch (OperationCanceledException) when (connectCts.IsCancellationRequested)
                {
                    _logger.LogWarning("Connection timeout for client {ClientId}", client.ClientId);
                    client.ConnectionFailureCount++;
                    return;
                }
                
                if (!tcpClient.Connected)
                {
                    _logger.LogWarning("Failed to connect to client {ClientId}", client.ClientId);
                    client.ConnectionFailureCount++;
                    return;
                }
                
                // Reset failure count on successful connection
                client.ConnectionFailureCount = 0;
                
                // Lấy stream
                var stream = tcpClient.GetStream();
                
                // Gửi lệnh đồng bộ
                string syncCommand = $"AUTH:{client.AuthToken} SILENT_SYNC";
                byte[] commandBytes = Encoding.UTF8.GetBytes(syncCommand);
                byte[] lengthBytes = BitConverter.GetBytes(commandBytes.Length);
                
                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, stoppingToken);
                await stream.WriteAsync(commandBytes, 0, commandBytes.Length, stoppingToken);
                await stream.FlushAsync(stoppingToken);
                
                // Đọc phản hồi
                byte[] responseHeaderBuffer = new byte[4];
                int bytesRead = await ReadBytesAsync(stream, responseHeaderBuffer, 0, 4, TimeSpan.FromSeconds(10));
                
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Incomplete response header from client {ClientId}", client.ClientId);
                    return;
                }
                
                int responseLength = BitConverter.ToInt32(responseHeaderBuffer, 0);
                
                if (responseLength <= 0 || responseLength > 1024 * 1024) // Giới hạn 1MB
                {
                    _logger.LogWarning("Invalid response length from client {ClientId}: {Length}", 
                        client.ClientId, responseLength);
                    return;
                }
                
                byte[] responseBuffer = new byte[responseLength];
                bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromSeconds(30));
                
                if (bytesRead < responseLength)
                {
                    _logger.LogWarning("Incomplete response from client {ClientId}", client.ClientId);
                    return;
                }
                
                string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                
                if (response == "READY_FOR_SILENT_SYNC")
                {
                    await PerformSilentSyncAsync(client, stream, stoppingToken);
                }
                else
                {
                    _logger.LogWarning("Unexpected response from client {ClientId}: {Response}", 
                        client.ClientId, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sync with client {ClientId}", client.ClientId);
                client.LastSyncError = ex.Message;
                client.SyncErrorCount++;
            }
            finally
            {
                tcpClient?.Close();
                
                // Lưu trạng thái của client
                await SaveRegisteredClientsAsync();
            }
        }

        /// <summary>
        /// Thực hiện đồng bộ âm thầm với client
        /// </summary>
        private async Task PerformSilentSyncAsync(ClientRegistration client, NetworkStream stream, CancellationToken stoppingToken)
        {
            try
            {
                // Lấy danh sách profile
                var profiles = _profileManager.GetAllProfiles();
                
                if (profiles.Count == 0)
                {
                    _logger.LogInformation("No profiles to sync with client {ClientId}", client.ClientId);
                    return;
                }
                
                // Chuyển đổi thành JSON
                string json = JsonSerializer.Serialize(profiles);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                
                // Gửi kích thước dữ liệu
                byte[] lengthBytes = BitConverter.GetBytes(jsonBytes.Length);
                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, stoppingToken);
                
                // Gửi dữ liệu theo từng phần để xử lý dữ liệu lớn
                const int chunkSize = 8192; // 8KB chunks
                int sentBytes = 0;
                
                while (sentBytes < jsonBytes.Length)
                {
                    int bytesToSend = Math.Min(chunkSize, jsonBytes.Length - sentBytes);
                    await stream.WriteAsync(jsonBytes, sentBytes, bytesToSend, stoppingToken);
                    sentBytes += bytesToSend;
                    
                    // Báo cáo tiến trình
                    if (sentBytes % (1024 * 1024) == 0) // Mỗi 1MB
                    {
                        _logger.LogDebug("Sent {SentMB}MB / {TotalMB}MB to client {ClientId}", 
                            sentBytes / (1024 * 1024), jsonBytes.Length / (1024 * 1024), client.ClientId);
                    }
                }
                
                await stream.FlushAsync(stoppingToken);
                
                // Đọc phản hồi
                byte[] responseHeaderBuffer = new byte[4];
                int bytesRead = await ReadBytesAsync(stream, responseHeaderBuffer, 0, 4, TimeSpan.FromMinutes(1));
                
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Incomplete response header from client {ClientId} after sync", client.ClientId);
                    return;
                }
                
                int responseLength = BitConverter.ToInt32(responseHeaderBuffer, 0);
                
                if (responseLength <= 0 || responseLength > 1024 * 1024) // Giới hạn 1MB
                {
                    _logger.LogWarning("Invalid response length from client {ClientId}: {Length}", 
                        client.ClientId, responseLength);
                    return;
                }
                
                byte[] responseBuffer = new byte[responseLength];
                bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromMinutes(1));
                
                if (bytesRead < responseLength)
                {
                    _logger.LogWarning("Incomplete response from client {ClientId} after sync", client.ClientId);
                    return;
                }
                
                string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                
                if (response.StartsWith("SYNC_COMPLETE:"))
                {
                    // Parse result (format: "SYNC_COMPLETE:added:updated:errors")
                    var parts = response.Split(':');
                    if (parts.Length >= 4)
                    {
                        int added = int.Parse(parts[1]);
                        int updated = int.Parse(parts[2]);
                        int errors = int.Parse(parts[3]);
                        
                        _logger.LogInformation("Sync with client {ClientId} completed. Added: {Added}, Updated: {Updated}, Errors: {Errors}", 
                            client.ClientId, added, updated, errors);
                        
                        // Update client status
                        client.LastSuccessfulSync = DateTime.UtcNow;
                        client.LastSyncResults = $"Added: {added}, Updated: {updated}, Errors: {errors}";
                    }
                    else
                    {
                        _logger.LogWarning("Invalid SYNC_COMPLETE response format from client {ClientId}: {Response}", 
                            client.ClientId, response);
                    }
                }
                else if (response.StartsWith("ERROR:"))
                {
                    string error = response.Substring("ERROR:".Length);
                    _logger.LogWarning("Client {ClientId} reported error during sync: {Error}", client.ClientId, error);
                    client.LastSyncError = error;
                    client.SyncErrorCount++;
                }
                else
                {
                    _logger.LogWarning("Unexpected sync response from client {ClientId}: {Response}", 
                        client.ClientId, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during silent sync with client {ClientId}", client.ClientId);
                client.LastSyncError = ex.Message;
                client.SyncErrorCount++;
                throw;
            }
        }

        /// <summary>
        /// Đọc đúng số byte từ stream
        /// </summary>
        private async Task<int> ReadBytesAsync(NetworkStream stream, byte[] buffer, int offset, int count, TimeSpan timeout)
        {
            int totalBytesRead = 0;
            
            // Thiết lập timeout
            using var timeoutCts = new CancellationTokenSource(timeout);
            
            try
            {
                while (totalBytesRead < count)
                {
                    int bytesRead = await stream.ReadAsync(buffer, offset + totalBytesRead, count - totalBytesRead, 
                        timeoutCts.Token);
                    
                    if (bytesRead == 0)
                    {
                        // Connection closed
                        break;
                    }
                    
                    totalBytesRead += bytesRead;
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("Timeout reading from stream after receiving {BytesRead}/{ExpectedBytes} bytes", 
                    totalBytesRead, count);
            }
            
            return totalBytesRead;
        }

        /// <summary>
        /// Đăng ký client mới để đồng bộ
        /// </summary>
        public async Task<bool> RegisterClientAsync(ClientRegistration client)
        {
            if (string.IsNullOrEmpty(client.ClientId) || string.IsNullOrEmpty(client.Address))
            {
                _logger.LogWarning("Invalid client registration attempt with empty ID or address");
                return false;
            }
            
            lock (_registeredClients)
            {
                if (_registeredClients.ContainsKey(client.ClientId))
                {
                    // Cập nhật thông tin client nếu đã tồn tại
                    _registeredClients[client.ClientId] = client;
                    _logger.LogInformation("Updated registration for client {ClientId}", client.ClientId);
                }
                else
                {
                    // Thêm client mới
                    _registeredClients.Add(client.ClientId, client);
                    _logger.LogInformation("Registered new client {ClientId}", client.ClientId);
                }
            }
            
            // Lưu danh sách client
            await SaveRegisteredClientsAsync();
            return true;
        }

        /// <summary>
        /// Hủy đăng ký client
        /// </summary>
        public async Task<bool> UnregisterClientAsync(string clientId)
        {
            bool removed = false;
            
            lock (_registeredClients)
            {
                if (_registeredClients.ContainsKey(clientId))
                {
                    _registeredClients.Remove(clientId);
                    removed = true;
                    _logger.LogInformation("Unregistered client {ClientId}", clientId);
                }
            }
            
            if (removed)
            {
                await SaveRegisteredClientsAsync();
            }
            
            return removed;
        }

        /// <summary>
        /// Lấy danh sách client đã đăng ký
        /// </summary>
        public List<ClientRegistration> GetRegisteredClients()
        {
            lock (_registeredClients)
            {
                return _registeredClients.Values.ToList();
            }
        }

        /// <summary>
        /// Lưu danh sách client đã đăng ký
        /// </summary>
        private async Task SaveRegisteredClientsAsync()
        {
            try
            {
                string filePath = Path.Combine(_syncFolder, "registered_clients.json");
                
                List<ClientRegistration> clients;
                lock (_registeredClients)
                {
                    clients = _registeredClients.Values.ToList();
                }
                
                string json = JsonSerializer.Serialize(clients, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);
                
                _logger.LogDebug("Saved {Count} registered clients to {FilePath}", clients.Count, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving registered clients");
            }
        }

        /// <summary>
        /// Tải danh sách client đã đăng ký
        /// </summary>
        private async Task LoadRegisteredClientsAsync()
        {
            try
            {
                string filePath = Path.Combine(_syncFolder, "registered_clients.json");
                
                if (!File.Exists(filePath))
                {
                    _logger.LogInformation("No registered clients file found at {FilePath}", filePath);
                    return;
                }
                
                string json = await File.ReadAllTextAsync(filePath);
                var clients = JsonSerializer.Deserialize<List<ClientRegistration>>(json);
                
                if (clients != null)
                {
                    lock (_registeredClients)
                    {
                        _registeredClients.Clear();
                        foreach (var client in clients)
                        {
                            _registeredClients[client.ClientId] = client;
                        }
                    }
                    
                    _logger.LogInformation("Loaded {Count} registered clients", clients.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading registered clients");
            }
        }
    }

    /// <summary>
    /// Thông tin đăng ký client
    /// </summary>
    public class ClientRegistration
    {
        // Thông tin cơ bản
        public string ClientId { get; set; }
        public string Description { get; set; }
        public string Address { get; set; }
        public int Port { get; set; } = 61188;
        public string AuthToken { get; set; } = "simple_auth_token";
        
        // Trạng thái
        public bool IsActive { get; set; } = true;
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public DateTime LastSuccessfulSync { get; set; }
        public DateTime LastSyncAttempt { get; set; }
        public string LastSyncResults { get; set; }
        public string LastSyncError { get; set; }
        
        // Cấu hình
        public int SyncIntervalMinutes { get; set; } = 60; // 1 giờ
        public bool PushOnly { get; set; } = false; // Chỉ đẩy dữ liệu đến client
        public bool PullOnly { get; set; } = false; // Chỉ kéo dữ liệu từ client
        
        // Thống kê
        public int ConnectionFailureCount { get; set; } = 0;
        public int SyncErrorCount { get; set; } = 0;
    }
}