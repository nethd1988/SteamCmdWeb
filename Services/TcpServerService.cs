using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
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
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private readonly AppProfileManager _profileManager;
        private TcpListener _listener;
        private readonly string _configFolder = Path.Combine(Directory.GetCurrentDirectory(), "Profiles");
        private readonly string _dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        private readonly string _authToken = "simple_auth_token"; // Thay đổi nếu cần bảo mật hơn

        // Theo dõi các client được kết nối để tái sử dụng
        private readonly ConcurrentDictionary<string, DateTime> _activeConnections = new ConcurrentDictionary<string, DateTime>();

        // Semaphore để giới hạn số kết nối đồng thời
        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(20, 20); // Tối đa 20 kết nối đồng thời

        // Buffer pool để giảm áp lực GC
        private readonly ConcurrentQueue<byte[]> _bufferPool = new ConcurrentQueue<byte[]>();
        private const int BufferSize = 8192; // 8KB buffer
        private const int MaxPoolSize = 100; // Tối đa 100 buffer trong pool

        // Cache đơn giản
        private readonly ConcurrentDictionary<string, CachedItem<List<string>>> _profileNamesCache =
            new ConcurrentDictionary<string, CachedItem<List<string>>>();

        public TcpServerService(ILogger<TcpServerService> logger, AppProfileManager profileManager)
        {
            _logger = logger;
            _profileManager = profileManager;

            if (!Directory.Exists(_configFolder))
            {
                Directory.CreateDirectory(_configFolder);
                _logger.LogInformation("Created Profiles directory at {Path}", _configFolder);
            }

            if (!Directory.Exists(_dataFolder))
            {
                Directory.CreateDirectory(_dataFolder);
                _logger.LogInformation("Created Data directory at {Path}", _dataFolder);
            }

            // Khởi tạo buffer pool
            for (int i = 0; i < 10; i++) // Khởi tạo 10 buffer ban đầu
            {
                _bufferPool.Enqueue(new byte[BufferSize]);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, 61188);
                _listener.Start();
                _logger.LogInformation("TCP Server started on port 61188");

                // Bắt đầu task dọn dẹp các kết nối đã hết hạn
                _ = CleanupExpiredConnectionsAsync(stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();

                    // Cấu hình client options để tối ưu hiệu suất
                    client.NoDelay = true; // Tắt thuật toán Nagle
                    client.ReceiveBufferSize = BufferSize;
                    client.SendBufferSize = BufferSize;
                    client.ReceiveTimeout = 10000; // 10 giây timeout
                    client.SendTimeout = 10000;

                    string clientId = $"{((IPEndPoint)client.Client.RemoteEndPoint).Address}:{Guid.NewGuid()}";
                    _activeConnections[clientId] = DateTime.UtcNow.AddMinutes(10); // Hết hạn sau 10 phút

                    _ = ProcessClientAsync(client, clientId, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TCP Server");
            }
        }

        private async Task ProcessClientAsync(TcpClient client, string clientId, CancellationToken stoppingToken)
        {
            // Đợi token semaphore
            await _connectionSemaphore.WaitAsync(stoppingToken);

            try
            {
                await HandleClientAsync(client, stoppingToken);
            }
            finally
            {
                // Xóa client khỏi danh sách kết nối hoạt động
                _activeConnections.TryRemove(clientId, out _);

                // Giải phóng token semaphore
                _connectionSemaphore.Release();

                try
                {
                    client.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing TCP client");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            NetworkStream stream = null;
            byte[] buffer = GetBufferFromPool();

            try
            {
                stream = client.GetStream();

                // Đọc tiền tố độ dài (4 byte)
                int bytesRead = await ReadBytesAsync(stream, buffer, 0, 4, stoppingToken);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Failed to read request length from client. Bytes read: {BytesRead}", bytesRead);
                    return;
                }

                int requestLength = BitConverter.ToInt32(buffer, 0);
                if (requestLength <= 0 || requestLength > 1024 * 1024) // Giới hạn kích thước để tránh tấn công
                {
                    _logger.LogWarning("Invalid request length: {Length}", requestLength);
                    return;
                }

                // Đọc dữ liệu yêu cầu dựa trên độ dài
                if (requestLength > buffer.Length)
                {
                    // Nếu yêu cầu quá lớn, tạo buffer mới
                    ReturnBufferToPool(buffer);
                    buffer = new byte[requestLength];
                }

                bytesRead = await ReadBytesAsync(stream, buffer, 0, requestLength, stoppingToken);
                if (bytesRead < requestLength)
                {
                    _logger.LogWarning("Connection closed by client before reading full request");
                    return;
                }

                // Chuyển dữ liệu thành chuỗi
                string request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                // Ghi lại yêu cầu nhưng ẩn thông tin nhạy cảm (mật khẩu)
                string logRequest = request;
                if (request.Contains("PASSWORD"))
                {
                    // Ẩn mật khẩu trong log
                    logRequest = "***PASSWORD DATA HIDDEN***";
                }
                _logger.LogInformation("Received request from client: {Request}", logRequest);

                // Kiểm tra xác thực
                if (request.StartsWith($"AUTH:{_authToken}"))
                {
                    request = request.Substring($"AUTH:{_authToken}".Length).Trim();

                    if (request == "PING")
                    {
                        await SendResponseAsync(stream, "PONG", stoppingToken);
                    }
                    else if (request == "SEND_PROFILES")
                    {
                        await HandleSendProfilesAsync(stream, stoppingToken);
                    }
                    else if (request == "GET_PROFILES")
                    {
                        await HandleGetProfilesAsync(stream, stoppingToken);
                    }
                    else if (request.StartsWith("GET_PROFILE_DETAILS "))
                    {
                        string profileName = request.Substring("GET_PROFILE_DETAILS ".Length).Trim();
                        await HandleGetProfileDetailsAsync(stream, profileName, stoppingToken);
                    }
                    else
                    {
                        await SendResponseAsync(stream, "INVALID_REQUEST", stoppingToken);
                        _logger.LogWarning("Invalid request from client: {Request}", request);
                    }
                }
                else
                {
                    await SendResponseAsync(stream, "AUTH_FAILED", stoppingToken);
                    _logger.LogWarning("Client authentication failed.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling client: {Message}", ex.Message);
            }
            finally
            {
                ReturnBufferToPool(buffer);
                stream?.Dispose();
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, string response, CancellationToken stoppingToken)
        {
            try
            {
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                byte[] lengthBytes = BitConverter.GetBytes(responseBytes.Length);

                // Gửi tiền tố độ dài
                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, stoppingToken);

                // Gửi dữ liệu phản hồi, sử dụng buffer lớn hơn để giảm số lần gửi
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, stoppingToken);
                await stream.FlushAsync(stoppingToken);

                // Log ngắn gọn hơn để tránh spam log file
                string logResponse = response.Length > 100 ? response.Substring(0, 100) + "..." : response;
                _logger.LogDebug("Sent response to client: {Response}", logResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending response to client: {Message}", ex.Message);
                throw;
            }
        }

        private async Task HandleSendProfilesAsync(NetworkStream stream, CancellationToken stoppingToken)
        {
            await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);
            _logger.LogInformation("Sent READY_TO_RECEIVE to client");

            string backupFolder = Path.Combine(_dataFolder, "Backup");
            if (!Directory.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
                _logger.LogInformation("Created Backup directory at {Path}", backupFolder);
            }

            int profileCount = 0;
            byte[] buffer = GetBufferFromPool();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Đọc tiền tố độ dài của profile
                    int bytesRead = await ReadBytesAsync(stream, buffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogInformation("Client finished sending profiles. Total profiles received: {Count}", profileCount);
                        break;
                    }

                    int profileLength = BitConverter.ToInt32(buffer, 0);
                    if (profileLength == 0)
                    {
                        _logger.LogInformation("Received {Count} profiles. Process completed.", profileCount);
                        break;
                    }

                    if (profileLength > 1024 * 1024) // Giới hạn kích thước
                    {
                        _logger.LogWarning("Profile data too large: {Length}", profileLength);
                        break;
                    }

                    // Đọc dữ liệu profile
                    byte[] profileBuffer = profileLength <= buffer.Length ? buffer : new byte[profileLength];
                    bytesRead = await ReadBytesAsync(stream, profileBuffer, 0, profileLength, stoppingToken);

                    if (bytesRead < profileLength)
                    {
                        _logger.LogWarning("Connection closed by client before receiving full profile data");
                        break;
                    }

                    string jsonProfile = Encoding.UTF8.GetString(profileBuffer, 0, bytesRead);
                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        ClientProfile profile = JsonSerializer.Deserialize<ClientProfile>(jsonProfile, options);

                        if (profile != null)
                        {
                            string filePath = Path.Combine(backupFolder, $"profile_{profile.Id}.json");
                            await File.WriteAllTextAsync(filePath, jsonProfile, stoppingToken);
                            profileCount++;
                            _logger.LogInformation("Saved profile {Name} (ID: {Id})", profile.Name, profile.Id);

                            // Cập nhật hoặc thêm profile vào danh sách chính
                            var existingProfile = _profileManager.GetProfileById(profile.Id);
                            if (existingProfile == null)
                            {
                                _profileManager.AddProfile(profile);
                            }
                            else
                            {
                                _profileManager.UpdateProfile(profile);
                            }

                            // Xóa cache để đảm bảo dữ liệu mới nhất
                            InvalidateProfileCache();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing profile JSON: {Message}", ex.Message);
                    }

                    // Nếu đã sử dụng buffer tạm, giải phóng nó
                    if (profileBuffer != buffer)
                    {
                        profileBuffer = null;
                    }
                }
            }
            finally
            {
                ReturnBufferToPool(buffer);
            }
        }

        private async Task HandleGetProfilesAsync(NetworkStream stream, CancellationToken stoppingToken)
        {
            string cacheKey = "all_profiles";

            // Kiểm tra cache trước
            if (_profileNamesCache.TryGetValue(cacheKey, out var cachedProfiles) && !cachedProfiles.IsExpired)
            {
                string response = string.Join(",", cachedProfiles.Item);
                await SendResponseAsync(stream, response, stoppingToken);
                _logger.LogInformation("Sent cached profile list to client");
                return;
            }

            var profiles = _profileManager.GetAllProfiles();
            string responseData;

            if (profiles.Count > 0)
            {
                var profileNames = profiles.Select(p => p.Name).ToArray();
                responseData = string.Join(",", profileNames);

                // Cache results
                _profileNamesCache[cacheKey] = new CachedItem<List<string>>(
                    profileNames.ToList(),
                    TimeSpan.FromMinutes(5) // Cache valid for 5 minutes
                );
            }
            else
            {
                responseData = "NO_PROFILES";
            }

            await SendResponseAsync(stream, responseData, stoppingToken);
        }

        private async Task HandleGetProfileDetailsAsync(NetworkStream stream, string profileName, CancellationToken stoppingToken)
        {
            var profile = _profileManager.GetProfileByName(profileName);

            if (profile != null)
            {
                // Sử dụng bộ nhớ đệm để giảm áp lực GC
                using var ms = new MemoryStream();
                await JsonSerializer.SerializeAsync(ms, profile, new JsonSerializerOptions { WriteIndented = false }, stoppingToken);
                ms.Position = 0;

                // Sử dụng ReadAsync để tránh copy bộ nhớ
                byte[] buffer = new byte[ms.Length];
                await ms.ReadAsync(buffer, 0, buffer.Length, stoppingToken);

                // Gửi trực tiếp bytes thay vì chuyển đổi qua string
                byte[] lengthBytes = BitConverter.GetBytes(buffer.Length);
                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, stoppingToken);
                await stream.WriteAsync(buffer, 0, buffer.Length, stoppingToken);
                await stream.FlushAsync(stoppingToken);

                _logger.LogInformation("Sent profile details for {Name}", profileName);
            }
            else
            {
                await SendResponseAsync(stream, "PROFILE_NOT_FOUND", stoppingToken);
                _logger.LogWarning("Profile {Name} not found", profileName);
            }
        }

        private async Task CleanupExpiredConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var expiredKeys = _activeConnections
                        .Where(kv => kv.Value < now)
                        .Select(kv => kv.Key)
                        .ToList();

                    foreach (var key in expiredKeys)
                    {
                        _activeConnections.TryRemove(key, out _);
                    }

                    if (expiredKeys.Count > 0)
                    {
                        _logger.LogInformation("Cleaned up {Count} expired connections", expiredKeys.Count);
                    }

                    // Tối ưu cache định kỳ
                    CleanupExpiredCache();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning up expired connections");
                }

                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }

        // Phương thức để đọc đúng số byte yêu cầu
        private async Task<int> ReadBytesAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;
            while (totalBytesRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalBytesRead, count - totalBytesRead, cancellationToken);
                if (bytesRead == 0)
                {
                    // Connection closed before all expected bytes were read
                    break;
                }
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }

        // Pool buffer management
        private byte[] GetBufferFromPool()
        {
            if (_bufferPool.TryDequeue(out var buffer))
            {
                return buffer;
            }
            return new byte[BufferSize];
        }

        private void ReturnBufferToPool(byte[] buffer)
        {
            if (buffer != null && buffer.Length == BufferSize && _bufferPool.Count < MaxPoolSize)
            {
                // Clear buffer before returning to pool
                Array.Clear(buffer, 0, buffer.Length);
                _bufferPool.Enqueue(buffer);
            }
        }

        // Cache invalidation
        private void InvalidateProfileCache()
        {
            _profileNamesCache.Clear();
        }

        private void CleanupExpiredCache()
        {
            var expiredCacheKeys = _profileNamesCache
                .Where(kv => kv.Value.IsExpired)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expiredCacheKeys)
            {
                _profileNamesCache.TryRemove(key, out _);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _listener?.Stop();
            _logger.LogInformation("TCP Server stopped");
            await base.StopAsync(cancellationToken);
        }

        // Lớp lưu trữ cache item có thời gian hết hạn
        private class CachedItem<T>
        {
            public T Item { get; }
            public DateTime Expiration { get; }
            public bool IsExpired => DateTime.UtcNow > Expiration;

            public CachedItem(T item, TimeSpan expirationTime)
            {
                Item = item;
                Expiration = DateTime.UtcNow.Add(expirationTime);
            }
        }
    }
}