using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    /// <summary>
    /// Lớp tiện ích hỗ trợ đồng bộ âm thầm
    /// </summary>
    public static class SilentSyncHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        static SilentSyncHelper()
        {
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// Đồng bộ âm thầm đến server qua HTTP API
        /// </summary>
        public static async Task<SilentSyncResult> SyncToServerViaHttpAsync(
            string serverUrl,
            List<ClientProfile> profiles,
            ILogger logger = null)
        {
            try
            {
                // Kiểm tra đầu vào
                if (string.IsNullOrEmpty(serverUrl))
                {
                    throw new ArgumentException("Server URL is required", nameof(serverUrl));
                }

                if (profiles == null || profiles.Count == 0)
                {
                    throw new ArgumentException("At least one profile is required", nameof(profiles));
                }

                // Log thông tin
                logger?.LogInformation("Starting silent sync via HTTP to {ServerUrl}. Profile count: {ProfileCount}",
                    serverUrl, profiles.Count);

                // Chuẩn bị endpoint
                string endpoint = serverUrl.EndsWith("/")
                    ? $"{serverUrl}api/silentsync/full"
                    : $"{serverUrl}/api/silentsync/full";

                // Chuyển đổi danh sách profile thành JSON
                string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = false });

                // Tạo request
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Gửi request
                HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);

                // Kiểm tra kết quả
                response.EnsureSuccessStatusCode();

                // Đọc response
                string responseContent = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var syncResponse = JsonSerializer.Deserialize<SyncResponse>(responseContent, options);

                // Log kết quả
                logger?.LogInformation("Silent sync via HTTP completed. Added: {Added}, Updated: {Updated}, Errors: {Errors}",
                    syncResponse.Added, syncResponse.Updated, syncResponse.Errors);

                // Trả về kết quả
                return new SilentSyncResult
                {
                    Success = syncResponse.Success,
                    Message = syncResponse.Message,
                    AddedProfiles = syncResponse.Added,
                    UpdatedProfiles = syncResponse.Updated,
                    FailedProfiles = syncResponse.Errors,
                    TotalProfiles = profiles.Count
                };
            }
            catch (HttpRequestException ex)
            {
                logger?.LogError(ex, "HTTP error during silent sync to {ServerUrl}: {Message}", serverUrl, ex.Message);
                return new SilentSyncResult
                {
                    Success = false,
                    Message = $"HTTP error: {ex.Message}",
                    Exception = ex
                };
            }
            catch (TaskCanceledException ex)
            {
                logger?.LogError(ex, "Timeout during silent sync to {ServerUrl}", serverUrl);
                return new SilentSyncResult
                {
                    Success = false,
                    Message = "Timeout during sync",
                    Exception = ex
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during silent sync to {ServerUrl}: {Message}", serverUrl, ex.Message);
                return new SilentSyncResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}",
                    Exception = ex
                };
            }
        }

        /// <summary>
        /// Đồng bộ âm thầm đến server qua TCP
        /// </summary>
        public static async Task<SilentSyncResult> SyncToServerViaTcpAsync(
            string serverAddress,
            int port,
            List<ClientProfile> profiles,
            string authToken = "simple_auth_token",
            int timeoutSeconds = 300,
            ILogger logger = null)
        {
            System.Net.Sockets.TcpClient client = null;

            try
            {
                // Kiểm tra đầu vào
                if (string.IsNullOrEmpty(serverAddress))
                {
                    throw new ArgumentException("Server address is required", nameof(serverAddress));
                }

                if (profiles == null || profiles.Count == 0)
                {
                    throw new ArgumentException("At least one profile is required", nameof(profiles));
                }

                // Log thông tin
                logger?.LogInformation("Starting silent sync via TCP to {ServerAddress}:{Port}. Profile count: {ProfileCount}",
                    serverAddress, port, profiles.Count);

                // Khởi tạo kết nối TCP
                client = new System.Net.Sockets.TcpClient();
                client.ReceiveTimeout = timeoutSeconds * 1000;
                client.SendTimeout = timeoutSeconds * 1000;

                // Connect đến server
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    // Sửa đổi để không sử dụng extension method
                    var connectTask = client.ConnectAsync(serverAddress, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(10))) != connectTask)
                    {
                        throw new TimeoutException($"Connection timeout to {serverAddress}:{port}");
                    }
                    await connectTask;
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"Connection timeout to {serverAddress}:{port}");
                }

                if (!client.Connected)
                {
                    throw new Exception($"Could not connect to {serverAddress}:{port}");
                }

                logger?.LogDebug("Connected to {ServerAddress}:{Port}", serverAddress, port);

                // Lấy network stream
                NetworkStream stream = client.GetStream();

                // Gửi lệnh SILENT_SYNC
                string command = $"AUTH:{authToken} SILENT_SYNC";
                byte[] commandBytes = Encoding.UTF8.GetBytes(command);
                byte[] lengthBytes = BitConverter.GetBytes(commandBytes.Length);

                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                await stream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await stream.FlushAsync();

                logger?.LogDebug("Sent SILENT_SYNC command to server");

                // Đọc phản hồi
                byte[] responseHeaderBuffer = new byte[4];
                int bytesRead = await ReadBytesAsync(stream, responseHeaderBuffer, 0, 4, TimeSpan.FromSeconds(30));

                if (bytesRead < 4)
                {
                    throw new IOException("Incomplete response header from server");
                }

                int responseLength = BitConverter.ToInt32(responseHeaderBuffer, 0);

                if (responseLength <= 0 || responseLength > 1024 * 1024) // Giới hạn 1MB
                {
                    throw new InvalidDataException($"Invalid response length: {responseLength}");
                }

                byte[] responseBuffer = new byte[responseLength];
                bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromSeconds(30));

                if (bytesRead < responseLength)
                {
                    throw new IOException("Incomplete response from server");
                }

                string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                if (response != "READY_FOR_SILENT_SYNC")
                {
                    throw new Exception($"Unexpected response from server: {response}");
                }

                logger?.LogDebug("Server is ready for silent sync");

                // Chuyển đổi danh sách profile thành JSON
                string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = false });
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

                // Gửi kích thước dữ liệu
                byte[] jsonLengthBytes = BitConverter.GetBytes(jsonBytes.Length);
                await stream.WriteAsync(jsonLengthBytes, 0, jsonLengthBytes.Length);

                // Gửi dữ liệu theo từng phần để xử lý dữ liệu lớn
                const int chunkSize = 8192; // 8KB chunks
                int sentBytes = 0;

                while (sentBytes < jsonBytes.Length)
                {
                    int bytesToSend = Math.Min(chunkSize, jsonBytes.Length - sentBytes);
                    await stream.WriteAsync(jsonBytes, sentBytes, bytesToSend);
                    sentBytes += bytesToSend;

                    // Báo cáo tiến trình
                    if (sentBytes % (1024 * 1024) == 0) // Mỗi 1MB
                    {
                        logger?.LogDebug("Sent {SentMB}MB / {TotalMB}MB to server",
                            sentBytes / (1024 * 1024), jsonBytes.Length / (1024 * 1024));
                    }
                }

                await stream.FlushAsync();

                logger?.LogDebug("Sent {TotalBytes} bytes of profile data to server", jsonBytes.Length);

                // Đọc phản hồi kết quả
                responseHeaderBuffer = new byte[4];
                bytesRead = await ReadBytesAsync(stream, responseHeaderBuffer, 0, 4, TimeSpan.FromMinutes(5));

                if (bytesRead < 4)
                {
                    throw new IOException("Incomplete result response header from server");
                }

                responseLength = BitConverter.ToInt32(responseHeaderBuffer, 0);

                if (responseLength <= 0 || responseLength > 1024 * 1024) // Giới hạn 1MB
                {
                    throw new InvalidDataException($"Invalid result response length: {responseLength}");
                }

                responseBuffer = new byte[responseLength];
                bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromMinutes(5));

                if (bytesRead < responseLength)
                {
                    throw new IOException("Incomplete result response from server");
                }

                response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                if (response.StartsWith("SYNC_COMPLETE:"))
                {
                    // Parse result (format: "SYNC_COMPLETE:added:updated:errors")
                    var parts = response.Split(':');
                    if (parts.Length >= 4)
                    {
                        int added = int.Parse(parts[1]);
                        int updated = int.Parse(parts[2]);
                        int errors = int.Parse(parts[3]);

                        logger?.LogInformation("Silent sync via TCP completed. Added: {Added}, Updated: {Updated}, Errors: {Errors}",
                            added, updated, errors);

                        return new SilentSyncResult
                        {
                            Success = true,
                            Message = $"Sync completed. Added: {added}, Updated: {updated}, Errors: {errors}",
                            AddedProfiles = added,
                            UpdatedProfiles = updated,
                            FailedProfiles = errors,
                            TotalProfiles = profiles.Count
                        };
                    }
                    else
                    {
                        throw new FormatException($"Invalid SYNC_COMPLETE response format: {response}");
                    }
                }
                else if (response.StartsWith("ERROR:"))
                {
                    string error = response.Substring("ERROR:".Length);
                    logger?.LogWarning("Server reported error during sync: {Error}", error);

                    return new SilentSyncResult
                    {
                        Success = false,
                        Message = $"Server reported error: {error}"
                    };
                }
                else
                {
                    throw new Exception($"Unexpected result response from server: {response}");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during silent sync via TCP to {ServerAddress}:{Port}: {Message}",
                    serverAddress, port, ex.Message);

                return new SilentSyncResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}",
                    Exception = ex
                };
            }
            finally
            {
                // Đóng kết nối
                client?.Close();
                client?.Dispose();
            }
        }

        /// <summary>
        /// Đọc đúng số byte từ stream
        /// </summary>
        private static async Task<int> ReadBytesAsync(NetworkStream stream, byte[] buffer, int offset, int count, TimeSpan timeout)
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
                throw new TimeoutException($"Timeout reading from stream after receiving {totalBytesRead}/{count} bytes");
            }

            return totalBytesRead;
        }
    }

    /// <summary>
    /// Kết quả của quá trình đồng bộ âm thầm
    /// </summary>
    public class SilentSyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int AddedProfiles { get; set; }
        public int UpdatedProfiles { get; set; }
        public int FailedProfiles { get; set; }
        public int TotalProfiles { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Phản hồi từ server sau khi đồng bộ
    /// </summary>
    public class SyncResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int TotalProfiles { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Loại bỏ TaskExtensions để tránh lỗi
}