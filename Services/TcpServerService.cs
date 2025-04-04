using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly ConcurrentDictionary<string, ClientConnection> _activeConnections = 
            new ConcurrentDictionary<string, ClientConnection>();

        // Semaphore để giới hạn số kết nối đồng thời
        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(20, 20); // Tối đa 20 kết nối đồng thời

        // Buffer pool để giảm áp lực GC
        private readonly ConcurrentQueue<byte[]> _bufferPool = new ConcurrentQueue<byte[]>();
        private const int BufferSize = 8192; // 8KB buffer
        private const int MaxPoolSize = 100; // Tối đa 100 buffer trong pool

        // Cache đơn giản
        private readonly ConcurrentDictionary<string, CachedItem<List<string>>> _profileNamesCache =
            new ConcurrentDictionary<string, CachedItem<List<string>>>();

        // Thống kê
        private long _totalConnections = 0;
        private long _totalProfilesReceived = 0;
        private long _totalFailedRequests = 0;
        private readonly ConcurrentBag<LogEntry> _serverLogs = new ConcurrentBag<LogEntry>();
        private const int MaxLogEntries = 1000; // Giới hạn số lượng log entries để tránh quá tải bộ nhớ

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

            // Thêm log khởi động
            AddLog(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = "TCP Server service initialized",
                Level = LogLevel.Information,
                Source = "System"
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, 61188);
                _listener.Start();
                
                _logger.LogInformation("TCP Server started on port 61188");
                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = "TCP Server started on port 61188",
                    Level = LogLevel.Information,
                    Source = "System"
                });

                // Bắt đầu task dọn dẹp các kết nối đã hết hạn
                _ = CleanupExpiredConnectionsAsync(stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _totalConnections);

                    // Cấu hình client options để tối ưu hiệu suất
                    client.NoDelay = true; // Tắt thuật toán Nagle
                    client.ReceiveBufferSize = BufferSize;
                    client.SendBufferSize = BufferSize;
                    client.ReceiveTimeout = 30000; // 30 giây timeout
                    client.SendTimeout = 30000;

                    string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    string clientId = $"{clientIp}:{Guid.NewGuid()}";
                    
                    var clientConnection = new ClientConnection
                    {
                        Client = client,
                        LastActivity = DateTime.UtcNow,
                        IpAddress = clientIp,
                        Id = clientId
                    };
                    
                    _activeConnections[clientId] = clientConnection;

                    AddLog(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = $"Client connected from {clientIp}",
                        Level = LogLevel.Information,
                        Source = "Connection"
                    });

                    _ = ProcessClientAsync(clientConnection, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TCP Server");
                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"TCP Server error: {ex.Message}",
                    Level = LogLevel.Error,
                    Source = "System"
                });
            }
        }

        private async Task ProcessClientAsync(ClientConnection connection, CancellationToken stoppingToken)
        {
            // Đợi token semaphore
            await _connectionSemaphore.WaitAsync(stoppingToken);

            try
            {
                await HandleClientAsync(connection, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing client {ClientId}: {Message}", connection.Id, ex.Message);
                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"Error processing client {connection.IpAddress}: {ex.Message}",
                    Level = LogLevel.Error,
                    Source = "ClientHandler"
                });
                Interlocked.Increment(ref _totalFailedRequests);
            }
            finally
            {
                // Xóa client khỏi danh sách kết nối hoạt động
                _activeConnections.TryRemove(connection.Id, out _);

                // Giải phóng token semaphore
                _connectionSemaphore.Release();

                try
                {
                    connection.Client?.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing TCP client {ClientId}", connection.Id);
                }
            }
        }

        private async Task HandleClientAsync(ClientConnection connection, CancellationToken stoppingToken)
        {
            NetworkStream stream = null;
            byte[] buffer = GetBufferFromPool();

            try
            {
                stream = connection.Client.GetStream();

                // Đọc tiền tố độ dài (4 byte)
                int bytesRead = await ReadBytesAsync(stream, buffer, 0, 4, stoppingToken);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Failed to read request length from client {ClientId}. Bytes read: {BytesRead}", 
                        connection.Id, bytesRead);
                    return;
                }

                int requestLength = BitConverter.ToInt32(buffer, 0);
                if (requestLength <= 0 || requestLength > 10 * 1024 * 1024) // Giới hạn kích thước để tránh tấn công (10MB)
                {
                    _logger.LogWarning("Invalid request length: {Length} from client {ClientId}", 
                        requestLength, connection.Id);
                    return;
                }

                // Đọc dữ liệu yêu cầu dựa trên độ dài
                byte[] requestBuffer;
                if (requestLength > buffer.Length)
                {
                    // Nếu yêu cầu quá lớn, tạo buffer mới
                    requestBuffer = new byte[requestLength];
                    ReturnBufferToPool(buffer);
                }
                else
                {
                    requestBuffer = buffer;
                }

                bytesRead = await ReadBytesAsync(stream, requestBuffer, 0, requestLength, stoppingToken);
                if (bytesRead < requestLength)
                {
                    _logger.LogWarning("Connection closed by client {ClientId} before reading full request", 
                        connection.Id);
                    return;
                }

                // Chuyển dữ liệu thành chuỗi
                string request = Encoding.UTF8.GetString(requestBuffer, 0, bytesRead).Trim();

                // Ghi lại yêu cầu nhưng ẩn thông tin nhạy cảm (mật khẩu)
                string logRequest = request;
                if (request.Contains("PASSWORD") || request.Contains("password"))
                {
                    // Ẩn mật khẩu trong log
                    logRequest = "***PASSWORD DATA HIDDEN***";
                }
                
                _logger.LogInformation("Received request from client {ClientId}: {Request}", 
                    connection.Id, logRequest);

                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"Request from {connection.IpAddress}: {(logRequest.Length > 100 ? logRequest.Substring(0, 100) + "..." : logRequest)}",
                    Level = LogLevel.Information,
                    Source = "Request"
                });

                // Cập nhật thời gian hoạt động cuối
                connection.LastActivity = DateTime.UtcNow;

                // Kiểm tra xác thực
                if (request.StartsWith($"AUTH:{_authToken}"))
                {
                    request = request.Substring($"AUTH:{_authToken}".Length).Trim();
                    await ProcessAuthenticatedRequestAsync(connection, request, stream, stoppingToken);
                }
                else
                {
                    await SendResponseAsync(stream, "AUTH_FAILED", stoppingToken);
                    _logger.LogWarning("Client {ClientId} authentication failed", connection.Id);
                    
                    AddLog(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = $"Authentication failed for client {connection.IpAddress}",
                        Level = LogLevel.Warning,
                        Source = "Auth"
                    });
                    
                    Interlocked.Increment(ref _totalFailedRequests);
                }
            }
            finally
            {
                if (buffer != null && buffer != requestBuffer)
                {
                    ReturnBufferToPool(buffer);
                }
                if (requestBuffer != null && requestBuffer != buffer)
                {
                    // Không trả requestBuffer vào pool vì kích thước có thể khác
                    requestBuffer = null;
                }
            }
        }

        private async Task ProcessAuthenticatedRequestAsync(
            ClientConnection connection, 
            string request, 
            NetworkStream stream, 
            CancellationToken stoppingToken)
        {
            try
            {
                if (request == "PING")
                {
                    await SendResponseAsync(stream, "PONG", stoppingToken);
                    AddLog(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = $"Ping request from {connection.IpAddress} - responded with PONG",
                        Level = LogLevel.Information,
                        Source = "Ping"
                    });
                }
                else if (request == "SEND_PROFILE")
                {
                    await HandleSendProfileAsync(connection, stream, stoppingToken);
                }
                else if (request == "SEND_PROFILES")
                {
                    await HandleSendProfilesAsync(connection, stream, stoppingToken);
                }
                else if (request == "GET_PROFILES")
                {
                    await HandleGetProfilesAsync(connection, stream, stoppingToken);
                }
                else if (request.StartsWith("GET_PROFILE_DETAILS "))
                {
                    string profileName = request.Substring("GET_PROFILE_DETAILS ".Length).Trim();
                    await HandleGetProfileDetailsAsync(connection, stream, profileName, stoppingToken);
                }
                else if (request.StartsWith("SILENT_SYNC"))
                {
                    await HandleSilentSyncAsync(connection, stream, stoppingToken);
                }
                else
                {
                    await SendResponseAsync(stream, "INVALID_REQUEST", stoppingToken);
                    _logger.LogWarning("Invalid request from client {ClientId}: {Request}", connection.Id, request);
                    
                    AddLog(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = $"Invalid request from {connection.IpAddress}: {request}",
                        Level = LogLevel.Warning,
                        Source = "Request"
                    });
                    
                    Interlocked.Increment(ref _totalFailedRequests);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing authenticated request from client {ClientId}: {Request}", 
                    connection.Id, request);
                
                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"Error processing request from {connection.IpAddress}: {ex.Message}",
                    Level = LogLevel.Error,
                    Source = "Request"
                });
                
                Interlocked.Increment(ref _totalFailedRequests);
                throw;
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
                _logger.LogDebug("Sent response: {Response}", logResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending response: {Message}", ex.Message);
                throw;
            }
        }

        private async Task HandleSendProfileAsync(ClientConnection connection, NetworkStream stream, CancellationToken stoppingToken)
        {
            // Trả lời client rằng server đã sẵn sàng nhận profile
            await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);
            _logger.LogInformation("Sent READY_TO_RECEIVE to client {ClientId}", connection.Id);

            AddLog(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = $"Ready to receive profile from {connection.IpAddress}",
                Level = LogLevel.Information,
                Source = "ProfileReceive"
            });

            byte[] buffer = GetBufferFromPool();
            try
            {
                // Đọc độ dài của profile
                int bytesRead = await ReadBytesAsync(stream, buffer, 0, 4, stoppingToken);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Failed to read profile length from client {ClientId}", connection.Id);
                    return;
                }

                int profileLength = BitConverter.ToInt32(buffer, 0);
                if (profileLength <= 0 || profileLength > 5 * 1024 * 1024) // Giới hạn 5MB
                {
                    _logger.LogWarning("Invalid profile length from client {ClientId}: {Length}", 
                        connection.Id, profileLength);
                    return;
                }

                // Đọc dữ liệu profile
                byte[] profileBuffer;
                if (profileLength > buffer.Length)
                {
                    profileBuffer = new byte[profileLength];
                    ReturnBufferToPool(buffer);
                }
                else
                {
                    profileBuffer = buffer;
                }

                bytesRead = await ReadBytesAsync(stream, profileBuffer, 0, profileLength, stoppingToken);
                if (bytesRead < profileLength)
                {
                    _logger.LogWarning("Connection closed by client {ClientId} before reading full profile", 
                        connection.Id);
                    return;
                }

                string jsonProfile = Encoding.UTF8.GetString(profileBuffer, 0, bytesRead);
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    ClientProfile profile = JsonSerializer.Deserialize<ClientProfile>(jsonProfile, options);

                    if (profile != null)
                    {
                        Interlocked.Increment(ref _totalProfilesReceived);

                        // Backup profile if needed
                        string backupFolder = Path.Combine(_dataFolder, "Backup");
                        if (!Directory.Exists(backupFolder))
                        {
                            Directory.CreateDirectory(backupFolder);
                        }

                        string filePath = Path.Combine(backupFolder, $"profile_{profile.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                        await File.WriteAllTextAsync(filePath, jsonProfile, stoppingToken);

                        // Thêm hoặc cập nhật profile
                        var existingProfile = _profileManager.GetProfileById(profile.Id);
                        if (existingProfile == null)
                        {
                            _profileManager.AddProfile(profile);
                            
                            _logger.LogInformation("Added new profile: {Name} (ID: {Id}) from client {ClientId}", 
                                profile.Name, profile.Id, connection.Id);
                            
                            AddLog(new LogEntry
                            {
                                Timestamp = DateTime.Now,
                                Message = $"Added new profile: {profile.Name} (ID: {profile.Id}) from {connection.IpAddress}",
                                Level = LogLevel.Information,
                                Source = "ProfileAdd"
                            });
                        }
                        else
                        {
                            _profileManager.UpdateProfile(profile);
                            
                            _logger.LogInformation("Updated profile: {Name} (ID: {Id}) from client {ClientId}", 
                                profile.Name, profile.Id, connection.Id);
                            
                            AddLog(new LogEntry
                            {
                                Timestamp = DateTime.Now,
                                Message = $"Updated profile: {profile.Name} (ID: {profile.Id}) from {connection.IpAddress}",
                                Level = LogLevel.Information,
                                Source = "ProfileUpdate"
                            });
                        }

                        // Xóa cache để đảm bảo dữ liệu mới nhất
                        InvalidateProfileCache();

                        // Trả kết quả thành công
                        await SendResponseAsync(stream, $"SUCCESS:{profile.Id}", stoppingToken);
                    }
                    else
                    {
                        _logger.LogWarning("Received null profile from client {ClientId}", connection.Id);
                        await SendResponseAsync(stream, "ERROR:NULL_PROFILE", stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing profile JSON from client {ClientId}", connection.Id);
                    await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
                    
                    AddLog(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = $"Error processing profile from {connection.IpAddress}: {ex.Message}",
                        Level = LogLevel.Error,
                        Source = "ProfileProcess"
                    });
                    
                    Interlocked.Increment(ref _totalFailedRequests);
                }
            }
            finally
            {
                if (profileBuffer != buffer)
                {
                    // Không trả profileBuffer vào pool vì kích thước có thể khác
                    profileBuffer = null;
                }
                else
                {
                    ReturnBufferToPool(buffer);
                }
            }
        }

        private async Task HandleSendProfilesAsync(ClientConnection connection, NetworkStream stream, CancellationToken stoppingToken)
        {
            await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);
            _logger.LogInformation("Sent READY_TO_RECEIVE to client {ClientId} for batch profiles", connection.Id);

            AddLog(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = $"Ready to receive multiple profiles from {connection.IpAddress}",
                Level = LogLevel.Information,
                Source = "ProfileBatchReceive"
            });

            string backupFolder = Path.Combine(_dataFolder, "Backup");
            if (!Directory.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
                _logger.LogInformation("Created Backup directory at {Path}", backupFolder);
            }

            int profileCount = 0;
            int errorCount = 0;
            byte[] buffer = GetBufferFromPool();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Đọc tiền tố độ dài của profile
                    int bytesRead = await ReadBytesAsync(stream, buffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogInformation("Client {ClientId} finished sending profiles. Total received: {Count}", 
                            connection.Id, profileCount);
                        break;
                    }

                    int profileLength = BitConverter.ToInt32(buffer, 0);
                    if (profileLength == 0)
                    {
                        _logger.LogInformation("Received end marker (0) from client {ClientId}. Total profiles: {Count}", 
                            connection.Id, profileCount);
                        break;
                    }

                    if (profileLength < 0 || profileLength > 5 * 1024 * 1024) // Giới hạn 5MB
                    {
                        _logger.LogWarning("Invalid profile length from client {ClientId}: {Length}", 
                            connection.Id, profileLength);
                        errorCount++;
                        break;
                    }

                    // Đọc dữ liệu profile
                    byte[] profileBuffer;
                    if (profileLength > buffer.Length)
                    {
                        profileBuffer = new byte[profileLength];
                    }
                    else
                    {
                        profileBuffer = buffer;
                    }

                    bytesRead = await ReadBytesAsync(stream, profileBuffer, 0, profileLength, stoppingToken);

                    if (bytesRead < profileLength)
                    {
                        _logger.LogWarning("Connection closed by client {ClientId} before receiving full profile data", 
                            connection.Id);
                        errorCount++;
                        break;
                    }

                    string jsonProfile = Encoding.UTF8.GetString(profileBuffer, 0, bytesRead);
                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        ClientProfile profile = JsonSerializer.Deserialize<ClientProfile>(jsonProfile, options);

                        if (profile != null)
                        {
                            // Backup profile
                            string filePath = Path.Combine(backupFolder, $"profile_{profileCount}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                            await File.WriteAllTextAsync(filePath, jsonProfile, stoppingToken);

                            // Cập nhật hoặc thêm profile
                            var existingProfile = _profileManager.GetProfileById(profile.Id);
                            if (existingProfile == null)
                            {
                                _profileManager.AddProfile(profile);
                            }
                            else
                            {
                                _profileManager.UpdateProfile(profile);
                            }

                            profileCount++;
                            Interlocked.Increment(ref _totalProfilesReceived);

                            _logger.LogDebug("Processed profile {Count}: {Name} (ID: {Id}) from client {ClientId}", 
                                profileCount, profile.Name, profile.Id, connection.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing profile JSON at index {Count} from client {ClientId}", 
                            profileCount, connection.Id);
                        errorCount++;
                    }

                    // Nếu đã sử dụng buffer tạm, giải phóng nó
                    if (profileBuffer != buffer)
                    {
                        profileBuffer = null;
                    }
                }

                // Xóa cache để đảm bảo dữ liệu mới nhất
                InvalidateProfileCache();

                // Log kết quả
                _logger.LogInformation("Received {Count} profiles from client {ClientId}. Errors: {ErrorCount}", 
                    profileCount, connection.Id, errorCount);
                
                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"Received {profileCount} profiles from {connection.IpAddress}. Errors: {errorCount}",
                    Level = LogLevel.Information,
                    Source = "ProfileBatch"
                });

                // Trả kết quả cho client
                await SendResponseAsync(stream, $"DONE:{profileCount}:{errorCount}", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch profile receive from client {ClientId}", connection.Id);
                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"Error receiving batch profiles from {connection.IpAddress}: {ex.Message}",
                    Level = LogLevel.Error,
                    Source = "ProfileBatch"
                });
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
            }
            finally
            {
                ReturnBufferToPool(buffer);
            }
        }

        private async Task HandleSilentSyncAsync(ClientConnection connection, NetworkStream stream, CancellationToken stoppingToken)
        {
            await SendResponseAsync(stream, "READY_FOR_SILENT_SYNC", stoppingToken);
            _logger.LogInformation("Starting silent sync with client {ClientId}", connection.Id);
            
            AddLog(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = $"Starting silent sync with client {connection.IpAddress}",
                Level = LogLevel.Information,
                Source = "SilentSync"
            });

            byte[] buffer = GetBufferFromPool();
            try
            {
                // Đọc độ dài của batch JSON
                int bytesRead = await ReadBytesAsync(stream, buffer, 0, 4, stoppingToken);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Failed to read batch data length from client {ClientId}", connection.Id);
                    return;
                }

                int dataLength = BitConverter.ToInt32(buffer, 0);
                if (dataLength <= 0 || dataLength > 50 * 1024 * 1024) // Giới hạn 50MB
                {
                    _logger.LogWarning("Invalid batch data length from client {ClientId}: {Length}", 
                        connection.Id, dataLength);
                    return;
                }

                // Đọc batch JSON
                byte[] dataBuffer = new byte[dataLength];
                int totalRead = 0;
                int readSize = Math.Min(buffer.Length, dataLength);
                
                // Đọc từng phần để xử lý batch lớn
                while (totalRead < dataLength)
                {
                    int toRead = Math.Min(readSize, dataLength - totalRead);
                    bytesRead = await ReadBytesAsync(stream, buffer, 0, toRead, stoppingToken);
                    if (bytesRead <= 0) break;
                    
                    Buffer.BlockCopy(buffer, 0, dataBuffer, totalRead, bytesRead);
                    totalRead += bytesRead;
                    
                    // Báo cáo tiến trình cho client nếu cần
                    if (totalRead % (1024 * 1024) == 0) // Mỗi 1MB
                    {
                        _logger.LogDebug("Received {ReceivedMB}MB / {TotalMB}MB from client {ClientId}", 
                            totalRead / (1024 * 1024), dataLength / (1024 * 1024), connection.Id);
                    }
                }
                
                if (totalRead < dataLength)
                {
                    _logger.LogWarning("Incomplete data received from client {ClientId}: {Received}/{Total} bytes", 
                        connection.Id, totalRead, dataLength);
                    await SendResponseAsync(stream, "ERROR:INCOMPLETE_DATA", stoppingToken);
                    return;
                }

                // Xử lý dữ liệu
                string jsonData = Encoding.UTF8.GetString(dataBuffer, 0, totalRead);
                try
                {
                    var options = new JsonSerializerOptions { 
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true
                    };
                    
                    var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(jsonData, options);
                    
                    if (profiles == null || profiles.Count == 0)
                    {
                        _logger.LogWarning("Received empty profile list from client {ClientId}", connection.Id);
                        await SendResponseAsync(stream, "ERROR:EMPTY_DATA", stoppingToken);
                        return;
                    }
                    
                    _logger.LogInformation("Processing {Count} profiles from silent sync with client {ClientId}", 
                        profiles.Count, connection.Id);
                    
                    // Lưu trữ dữ liệu gốc
                    string backupFolder = Path.Combine(_dataFolder, "SilentSync");
                    if (!Directory.Exists(backupFolder))
                    {
                        Directory.CreateDirectory(backupFolder);
                    }
                    
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backupPath = Path.Combine(backupFolder, $"sync_{connection.IpAddress}_{timestamp}.json");
                    await File.WriteAllTextAsync(backupPath, jsonData, stoppingToken);
                    
                    // Xử lý từng profile
                    int addedCount = 0;
                    int updatedCount = 0;
                    int errorCount = 0;
                    
                    foreach (var profile in profiles)
                    {
                        try
                        {
                            if (profile == null) continue;
                            
                            var existingProfile = _profileManager.GetProfileById(profile.Id);
                            
                            if (existingProfile == null)
                            {
                                _profileManager.AddProfile(profile);
                                addedCount++;
                            }
                            else
                            {
                                _profileManager.UpdateProfile(profile);
                                updatedCount++;
                            }
                            
                            Interlocked.Increment(ref _totalProfilesReceived);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing profile {Id} during silent sync", profile?.Id);
                            errorCount++;
                        }
                    }
                    
                    // Xóa cache
                    InvalidateProfileCache();
                    
                    // Log kết quả
                    _logger.LogInformation("Silent sync completed. Added: {AddedCount}, Updated: {UpdatedCount}, Errors: {ErrorCount}", 
                        addedCount, updatedCount, errorCount);
                    
                    AddLog(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = $"Silent sync from {connection.IpAddress}: Added {addedCount}, Updated {updatedCount}, Errors {errorCount}",
                        Level = LogLevel.Information,
                        Source = "SilentSync"
                    });
                    
                    // Trả kết quả cho client
                    await SendResponseAsync(stream, $"SYNC_COMPLETE:{addedCount}:{updatedCount}:{errorCount}", stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing batch data from client {ClientId}", connection.Id);
                    AddLog(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = $"Error in silent sync from {connection.IpAddress}: {ex.Message}",
                        Level = LogLevel.Error,
                        Source = "SilentSync"
                    });
                    await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
                }
            }
            finally
            {
                ReturnBufferToPool(buffer);
            }
        }

        private async Task HandleGetProfilesAsync(ClientConnection connection, NetworkStream stream, CancellationToken stoppingToken)
        {
            string cacheKey = "all_profiles";

            // Kiểm tra cache trước
            if (_profileNamesCache.TryGetValue(cacheKey, out var cachedProfiles) && !cachedProfiles.IsExpired)
            {
                string response = string.Join(",", cachedProfiles.Item);
                await SendResponseAsync(stream, response, stoppingToken);
                _logger.LogInformation("Sent cached profile list to client {ClientId}", connection.Id);
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
            
            AddLog(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = $"Sent profile list ({profiles.Count} profiles) to {connection.IpAddress}",
                Level = LogLevel.Information,
                Source = "GetProfiles"
            });
        }

        private async Task HandleGetProfileDetailsAsync(ClientConnection connection, NetworkStream stream, string profileName, CancellationToken stoppingToken)
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

                _logger.LogInformation("Sent profile details for {Name} to client {ClientId}", 
                    profileName, connection.Id);
                
                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"Sent profile details for '{profileName}' to {connection.IpAddress}",
                    Level = LogLevel.Information,
                    Source = "GetProfileDetails"
                });
            }
            else
            {
                await SendResponseAsync(stream, "PROFILE_NOT_FOUND", stoppingToken);
                _logger.LogWarning("Profile {Name} not found for client {ClientId}", 
                    profileName, connection.Id);
                
                AddLog(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = $"Profile '{profileName}' not found - requested by {connection.IpAddress}",
                    Level = LogLevel.Warning,
                    Source = "GetProfileDetails"
                });
            }
        }

        private async Task CleanupExpiredConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var expiredConnections = _activeConnections
                        .Where(kv => (now - kv.Value.LastActivity).TotalMinutes > 10) // 10 phút không hoạt động
                        .ToList();

                    foreach (var conn in expiredConnections)
                    {
                        if (_activeConnections.TryRemove(conn.Key, out var connection))
                        {
                            try
                            {
                                connection.Client?.Close();
                                _logger.LogInformation("Closed expired connection from {IpAddress} (inactive for >10 min)", 
                                    connection.IpAddress);
                                
                                AddLog(new LogEntry
                                {
                                    Timestamp = DateTime.Now,
                                    Message = $"Closed inactive connection from {connection.IpAddress}",
                                    Level = LogLevel.Information,
                                    Source = "Cleanup"
                                });
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error closing expired connection from {IpAddress}", 
                                    connection.IpAddress);
                            }
                        }
                    }

                    if (expiredConnections.Count > 0)
                    {
                        _logger.LogInformation("Cleaned up {Count} expired connections", expiredConnections.Count);
                    }

                    // Tối ưu cache định kỳ
                    CleanupExpiredCache();
                    
                    // Dọn dẹp log entries
                    CleanupOldLogs();
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
            
            // Đặt timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            try
            {
                while (totalBytesRead < count)
                {
                    int bytesRead = await stream.ReadAsync(buffer, offset + totalBytesRead, count - totalBytesRead, 
                        linkedCts.Token);
                    
                    if (bytesRead == 0)
                    {
                        // Connection closed before all expected bytes were read
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
        
        private void CleanupOldLogs()
        {
            // Giới hạn số lượng log entries
            while (_serverLogs.Count > MaxLogEntries)
            {
                try
                {
                    // Lấy log entries cũ nhất và xóa
                    var orderedLogs = _serverLogs.OrderBy(l => l.Timestamp).ToList();
                    int removeCount = _serverLogs.Count - MaxLogEntries;
                    
                    for (int i = 0; i < removeCount && i < orderedLogs.Count; i++)
                    {
                        LogEntry entry = orderedLogs[i];
                        _serverLogs.TryTake(out _);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning up old logs");
                    break;
                }
            }
        }
        
        private void AddLog(LogEntry entry)
        {
            if (_serverLogs.Count < MaxLogEntries)
            {
                _serverLogs.Add(entry);
            }
            else
            {
                // Nếu đã đầy, xóa một log cũ nhất rồi thêm mới
                CleanupOldLogs();
                _serverLogs.Add(entry);
            }
        }

        public List<LogEntry> GetRecentLogs(int count = 100)
        {
            return _serverLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToList();
        }

        public ServerStats GetServerStats()
        {
            return new ServerStats
            {
                ActiveConnections = _activeConnections.Count,
                TotalConnections = _totalConnections,
                TotalProfilesReceived = _totalProfilesReceived,
                TotalFailedRequests = _totalFailedRequests,
                StartTime = _startTime,
                ServerPort = 61188,
                BufferPoolSize = _bufferPool.Count,
                CacheEntries = _profileNamesCache.Count
            };
        }

        private readonly DateTime _startTime = DateTime.Now;

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _listener?.Stop();
            _logger.LogInformation("TCP Server stopped");
            
            AddLog(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = "TCP Server stopped",
                Level = LogLevel.Information,
                Source = "System"
            });
            
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
        
        // Lớp để theo dõi kết nối client
        private class ClientConnection
        {
            public TcpClient Client { get; set; }
            public DateTime LastActivity { get; set; }
            public string IpAddress { get; set; }
            public string Id { get; set; }
        }
    }
    
    // Lớp lưu trữ log entry
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public LogLevel Level { get; set; }
        public string Source { get; set; }
    }
    
    // Lớp lưu trữ thống kê server
    public class ServerStats
    {
        public int ActiveConnections { get; set; }
        public long TotalConnections { get; set; }
        public long TotalProfilesReceived { get; set; }
        public long TotalFailedRequests { get; set; }
        public DateTime StartTime { get; set; }
        public int ServerPort { get; set; }
        public int BufferPoolSize { get; set; }
        public int CacheEntries { get; set; }
        
        public TimeSpan Uptime => DateTime.Now - StartTime;
    }
}