using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
    /// Dịch vụ đồng bộ với các client thông qua TCP
    /// </summary>
    public class ClientSyncService
    {
        private readonly ILogger<ClientSyncService> _logger;
        private readonly AppProfileManager _profileManager;
        private readonly string _defaultAuthToken = "simple_auth_token";
        private readonly string _configFolder;

        /// <summary>
        /// Khởi tạo dịch vụ đồng bộ client
        /// </summary>
        public ClientSyncService(ILogger<ClientSyncService> logger, AppProfileManager profileManager)
        {
            _logger = logger;
            _profileManager = profileManager;
            _configFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "ClientSync");

            if (!Directory.Exists(_configFolder))
            {
                Directory.CreateDirectory(_configFolder);
                _logger.LogInformation("Đã tạo thư mục ClientSync: {Path}", _configFolder);
            }
        }

        /// <summary>
        /// Kết nối tới server và đồng bộ dữ liệu
        /// </summary>
        public async Task<(bool Success, string Message)> SyncToServerAsync(
            string serverAddress,
            int port,
            string authToken,
            List<ClientProfile> profiles,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(serverAddress))
            {
                return (false, "Địa chỉ server không được để trống");
            }

            if (profiles == null || profiles.Count == 0)
            {
                return (false, "Không có profiles để đồng bộ");
            }

            if (string.IsNullOrEmpty(authToken))
            {
                authToken = _defaultAuthToken;
                _logger.LogWarning("Sử dụng token đồng bộ mặc định vì không được cung cấp");
            }

            using TcpClient client = new TcpClient();
            try
            {
                _logger.LogInformation("Kết nối đến server {ServerAddress}:{Port}", serverAddress, port);

                // Thiết lập timeout cho kết nối
                var connectTask = client.ConnectAsync(serverAddress, port);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                // Chờ kết nối hoặc timeout
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    return (false, $"Kết nối đến {serverAddress}:{port} đã hết thời gian chờ");
                }

                // Đảm bảo kết nối đã hoàn thành
                await connectTask;

                // Kiểm tra kết nối thành công
                if (!client.Connected)
                {
                    return (false, $"Không thể kết nối đến {serverAddress}:{port}");
                }

                _logger.LogInformation("Đã kết nối đến server {ServerAddress}:{port}", serverAddress, port);

                // Thực hiện đồng bộ
                return await SyncProfilesWithServer(client, authToken, profiles, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Đồng bộ bị hủy");
                return (false, "Đồng bộ bị hủy");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ với server: {Message}", ex.Message);
                return (false, $"Lỗi khi đồng bộ: {ex.Message}");
            }
        }

        /// <summary>
        /// Thực hiện đồng bộ profiles với server
        /// </summary>
        private async Task<(bool Success, string Message)> SyncProfilesWithServer(
            TcpClient client,
            string authToken,
            List<ClientProfile> profiles,
            CancellationToken cancellationToken)
        {
            NetworkStream stream = client.GetStream();

            try
            {
                // Gửi lệnh với token
                string command = $"AUTH:{authToken} SEND_PROFILES";
                byte[] commandBytes = Encoding.UTF8.GetBytes(command);

                // Gửi độ dài + nội dung command
                byte[] lengthBytes = BitConverter.GetBytes(commandBytes.Length);
                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
                await stream.WriteAsync(commandBytes, 0, commandBytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);

                _logger.LogDebug("Đã gửi lệnh đồng bộ ({Command})", command);

                // Đọc phản hồi ban đầu từ server
                byte[] headerBuffer = new byte[4];
                int bytesRead = await ReadBytesAsync(stream, headerBuffer, 0, 4, TimeSpan.FromSeconds(30), cancellationToken);

                if (bytesRead < 4)
                {
                    return (false, "Không nhận được phản hồi đầy đủ từ server");
                }

                int responseLength = BitConverter.ToInt32(headerBuffer, 0);

                if (responseLength <= 0 || responseLength > 1024)
                {
                    return (false, $"Độ dài phản hồi không hợp lệ: {responseLength}");
                }

                byte[] responseBuffer = new byte[responseLength];
                bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromSeconds(30), cancellationToken);

                if (bytesRead < responseLength)
                {
                    return (false, "Phản hồi từ server không đầy đủ");
                }

                string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                if (response != "READY_TO_RECEIVE")
                {
                    return (false, $"Phản hồi không mong muốn từ server: {response}");
                }

                _logger.LogDebug("Server sẵn sàng nhận profiles");

                // Gửi từng profile một
                int successCount = 0;
                int failCount = 0;

                foreach (var profile in profiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return (false, "Đồng bộ bị hủy");
                    }

                    bool profileSent = await SendProfileToServer(stream, profile, cancellationToken);

                    if (profileSent)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }

                // Gửi marker kết thúc (0)
                await stream.WriteAsync(new byte[4], 0, 4, cancellationToken);
                await stream.FlushAsync(cancellationToken);

                // Đọc phản hồi cuối cùng
                headerBuffer = new byte[4];
                bytesRead = await ReadBytesAsync(stream, headerBuffer, 0, 4, TimeSpan.FromSeconds(30), cancellationToken);

                if (bytesRead < 4)
                {
                    return (false, "Không nhận được phản hồi cuối cùng từ server");
                }

                responseLength = BitConverter.ToInt32(headerBuffer, 0);

                if (responseLength <= 0 || responseLength > 1024)
                {
                    return (false, $"Độ dài phản hồi cuối cùng không hợp lệ: {responseLength}");
                }

                responseBuffer = new byte[responseLength];
                bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromSeconds(30), cancellationToken);

                if (bytesRead < responseLength)
                {
                    return (false, "Phản hồi cuối cùng từ server không đầy đủ");
                }

                response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                if (response.StartsWith("DONE:"))
                {
                    var parts = response.Split(':');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int completedCount))
                    {
                        _logger.LogInformation("Đồng bộ thành công: {CompletedCount} profiles", completedCount);
                        return (true, $"Đồng bộ thành công: {completedCount} profiles ({successCount} thành công, {failCount} thất bại)");
                    }
                }

                return (true, $"Đồng bộ hoàn tất: {successCount} profiles thành công, {failCount} profiles thất bại");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong quá trình đồng bộ: {Message}", ex.Message);
                return (false, $"Lỗi trong quá trình đồng bộ: {ex.Message}");
            }
        }

        /// <summary>
        /// Gửi một profile đến server
        /// </summary>
        private async Task<bool> SendProfileToServer(
            NetworkStream stream,
            ClientProfile profile,
            CancellationToken cancellationToken)
        {
            try
            {
                // Chuyển đổi profile thành JSON
                string json = JsonSerializer.Serialize(profile);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

                // Gửi độ dài và nội dung JSON
                byte[] lengthBytes = BitConverter.GetBytes(jsonBytes.Length);
                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
                await stream.WriteAsync(jsonBytes, 0, jsonBytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);

                _logger.LogDebug("Đã gửi profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);

                // Đọc phản hồi
                byte[] headerBuffer = new byte[4];
                int bytesRead = await ReadBytesAsync(stream, headerBuffer, 0, 4, TimeSpan.FromSeconds(30), cancellationToken);

                if (bytesRead < 4)
                {
                    _logger.LogWarning("Không nhận được phản hồi khi gửi profile {ProfileName}", profile.Name);
                    return false;
                }

                int responseLength = BitConverter.ToInt32(headerBuffer, 0);

                if (responseLength <= 0 || responseLength > 1024)
                {
                    _logger.LogWarning("Độ dài phản hồi không hợp lệ: {Length}", responseLength);
                    return false;
                }

                byte[] responseBuffer = new byte[responseLength];
                bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromSeconds(30), cancellationToken);

                if (bytesRead < responseLength)
                {
                    _logger.LogWarning("Phản hồi từ server không đầy đủ khi gửi profile {ProfileName}", profile.Name);
                    return false;
                }

                string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                if (response.StartsWith("SUCCESS:"))
                {
                    var parts = response.Split(':');
                    _logger.LogInformation("Đã gửi thành công profile {ProfileName}", profile.Name);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Gửi profile {ProfileName} thất bại. Phản hồi: {Response}", profile.Name, response);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi profile {ProfileName}: {Message}", profile.Name, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Đọc dữ liệu từ stream với timeout
        /// </summary>
        private static async Task<int> ReadBytesAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            int totalBytesRead = 0;

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(timeout))
            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                try
                {
                    CancellationToken combinedToken = linkedCts.Token;

                    while (totalBytesRead < count)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, offset + totalBytesRead, count - totalBytesRead, combinedToken);

                        if (bytesRead == 0)
                        {
                            // Kết nối đã đóng
                            break;
                        }

                        totalBytesRead += bytesRead;
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    // Timeout
                    throw new TimeoutException($"Hết thời gian chờ khi đọc dữ liệu từ stream sau khi đã nhận {totalBytesRead}/{count} bytes");
                }
            }

            return totalBytesRead;
        }

        /// <summary>
        /// Đồng bộ silent đến server
        /// </summary>
        public async Task<(bool Success, string Message)> SilentSyncToServerAsync(
            string serverAddress,
            int port,
            List<ClientProfile> profiles,
            string authToken = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(serverAddress))
            {
                return (false, "Địa chỉ server không được để trống");
            }

            if (profiles == null || profiles.Count == 0)
            {
                return (false, "Không có profiles để đồng bộ");
            }

            if (string.IsNullOrEmpty(authToken))
            {
                authToken = _defaultAuthToken;
            }

            try
            {
                _logger.LogInformation("Bắt đầu silent sync đến {ServerAddress}:{Port}, {Count} profiles",
                    serverAddress, port, profiles.Count);

                // Lưu trước một bản sao local
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"silent_sync_{timestamp}.json";
                string filePath = Path.Combine(_configFolder, fileName);

                await File.WriteAllTextAsync(filePath,
                    JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }),
                    cancellationToken);

                // Thử kết nối TCP
                using TcpClient client = new TcpClient();

                try
                {
                    // Thiết lập timeout cho kết nối
                    var connectTask = client.ConnectAsync(serverAddress, port);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                    // Chờ kết nối hoặc timeout
                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                    if (completedTask == timeoutTask)
                    {
                        return (false, $"Kết nối đến {serverAddress}:{port} đã hết thời gian chờ");
                    }

                    // Đảm bảo kết nối đã hoàn thành
                    await connectTask;

                    if (!client.Connected)
                    {
                        return (false, $"Không thể kết nối đến {serverAddress}:{port}");
                    }

                    _logger.LogInformation("Đã kết nối đến server {ServerAddress}:{Port} cho silent sync",
                        serverAddress, port);

                    // Tiến hành silent sync
                    NetworkStream stream = client.GetStream();

                    // Gửi lệnh với token
                    string command = $"AUTH:{authToken} SILENT_SYNC";
                    byte[] commandBytes = Encoding.UTF8.GetBytes(command);

                    // Gửi độ dài + nội dung command
                    byte[] lengthBytes = BitConverter.GetBytes(commandBytes.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
                    await stream.WriteAsync(commandBytes, 0, commandBytes.Length, cancellationToken);
                    await stream.FlushAsync(cancellationToken);

                    _logger.LogDebug("Đã gửi lệnh silent sync");

                    // Đọc phản hồi ban đầu từ server
                    byte[] headerBuffer = new byte[4];
                    int bytesRead = await ReadBytesAsync(stream, headerBuffer, 0, 4, TimeSpan.FromSeconds(30), cancellationToken);

                    if (bytesRead < 4)
                    {
                        return (false, "Không nhận được phản hồi đầy đủ từ server");
                    }

                    int responseLength = BitConverter.ToInt32(headerBuffer, 0);

                    if (responseLength <= 0 || responseLength > 1024)
                    {
                        return (false, $"Độ dài phản hồi không hợp lệ: {responseLength}");
                    }

                    byte[] responseBuffer = new byte[responseLength];
                    bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromSeconds(30), cancellationToken);

                    if (bytesRead < responseLength)
                    {
                        return (false, "Phản hồi từ server không đầy đủ");
                    }

                    string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                    if (response != "READY_FOR_SILENT_SYNC")
                    {
                        return (false, $"Phản hồi không mong muốn từ server: {response}");
                    }

                    _logger.LogDebug("Server sẵn sàng cho silent sync");

                    // Gửi toàn bộ dữ liệu cùng lúc
                    string jsonData = JsonSerializer.Serialize(profiles);
                    byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonData);

                    // Gửi độ dài trước
                    lengthBytes = BitConverter.GetBytes(jsonBytes.Length);
                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);

                    // Gửi dữ liệu theo từng phần (để hỗ trợ dữ liệu lớn)
                    const int chunkSize = 8192; // 8KB mỗi đoạn
                    int totalSent = 0;

                    while (totalSent < jsonBytes.Length)
                    {
                        int remaining = jsonBytes.Length - totalSent;
                        int toSend = Math.Min(remaining, chunkSize);

                        await stream.WriteAsync(jsonBytes, totalSent, toSend, cancellationToken);
                        totalSent += toSend;

                        if (totalSent % (1024 * 1024) == 0) // Log mỗi 1MB
                        {
                            _logger.LogDebug("Đã gửi {SentMB}MB / {TotalMB}MB",
                                totalSent / (1024 * 1024),
                                (jsonBytes.Length + 1024 * 1024 - 1) / (1024 * 1024));
                        }
                    }

                    await stream.FlushAsync(cancellationToken);

                    _logger.LogInformation("Đã gửi {TotalBytes} bytes data cho silent sync", jsonBytes.Length);

                    // Đọc phản hồi kết quả
                    headerBuffer = new byte[4];
                    bytesRead = await ReadBytesAsync(stream, headerBuffer, 0, 4, TimeSpan.FromMinutes(2), cancellationToken);

                    if (bytesRead < 4)
                    {
                        return (false, "Không nhận được phản hồi kết quả từ server");
                    }

                    responseLength = BitConverter.ToInt32(headerBuffer, 0);

                    if (responseLength <= 0 || responseLength > 1024)
                    {
                        return (false, $"Độ dài phản hồi kết quả không hợp lệ: {responseLength}");
                    }

                    responseBuffer = new byte[responseLength];
                    bytesRead = await ReadBytesAsync(stream, responseBuffer, 0, responseLength, TimeSpan.FromMinutes(2), cancellationToken);

                    if (bytesRead < responseLength)
                    {
                        return (false, "Phản hồi kết quả từ server không đầy đủ");
                    }

                    response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                    if (response.StartsWith("SYNC_COMPLETE:"))
                    {
                        var parts = response.Split(':');
                        if (parts.Length >= 4)
                        {
                            if (int.TryParse(parts[1], out int added) &&
                                int.TryParse(parts[2], out int updated) &&
                                int.TryParse(parts[3], out int errors))
                            {
                                _logger.LogInformation("Silent sync thành công: Thêm {Added}, Cập nhật {Updated}, Lỗi {Errors}",
                                    added, updated, errors);

                                return (true, $"Đồng bộ thành công: Thêm {added}, Cập nhật {updated}, Lỗi {errors}");
                            }
                        }

                        return (true, "Đồng bộ thành công");
                    }
                    else if (response.StartsWith("ERROR:"))
                    {
                        string error = response.Substring("ERROR:".Length);
                        _logger.LogWarning("Silent sync lỗi: {Error}", error);

                        return (false, $"Lỗi từ server: {error}");
                    }
                    else
                    {
                        _logger.LogWarning("Phản hồi không mong muốn: {Response}", response);
                        return (false, $"Phản hồi không xác định từ server: {response}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi thực hiện silent sync qua TCP: {Message}", ex.Message);
                    return (false, $"Lỗi khi thực hiện đồng bộ: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong quá trình đồng bộ silent: {Message}", ex.Message);
                return (false, $"Lỗi đồng bộ: {ex.Message}");
            }
        }

        /// <summary>
        /// Đồng bộ silent qua HTTP
        /// </summary>
        public async Task<(bool Success, string Message)> SilentSyncToServerViaHttpAsync(
            string serverUrl,
            List<ClientProfile> profiles,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(serverUrl))
            {
                return (false, "URL server không được để trống");
            }

            if (profiles == null || profiles.Count == 0)
            {
                return (false, "Không có profiles để đồng bộ");
            }

            try
            {
                _logger.LogInformation("Bắt đầu silent sync qua HTTP đến {ServerUrl}, {Count} profiles",
                    serverUrl, profiles.Count);

                // Lưu bản sao local
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"http_sync_{timestamp}.json";
                string filePath = Path.Combine(_configFolder, fileName);

                await File.WriteAllTextAsync(filePath,
                    JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }),
                    cancellationToken);

                // Chuẩn bị endpoint
                string endpoint = serverUrl.EndsWith("/")
                    ? $"{serverUrl}api/silentsync/full"
                    : $"{serverUrl}/api/silentsync/full";

                // Gửi yêu cầu HTTP
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5); // Timeout 5 phút

                    // Chuẩn bị content
                    string json = JsonSerializer.Serialize(profiles);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    // Gửi request
                    HttpResponseMessage response = await client.PostAsync(endpoint, content, cancellationToken);

                    // Kiểm tra kết quả
                    if (response.IsSuccessStatusCode)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();

                        try
                        {
                            var result = JsonSerializer.Deserialize<SyncResponse>(responseBody,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (result != null && result.Success)
                            {
                                _logger.LogInformation("Silent sync HTTP thành công: Thêm {Added}, Cập nhật {Updated}, Lỗi {Errors}",
                                    result.Added, result.Updated, result.Errors);

                                return (true, $"Đồng bộ thành công: Thêm {result.Added}, Cập nhật {result.Updated}, Lỗi {result.Errors}");
                            }
                            else
                            {
                                _logger.LogWarning("Silent sync HTTP thất bại: {Message}", result?.Message ?? "Không có thông báo");
                                return (false, $"Đồng bộ thất bại: {result?.Message ?? "Không có thông báo"}");
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "Lỗi khi phân tích kết quả đồng bộ: {Response}", responseBody);
                            return (false, $"Lỗi khi phân tích kết quả: {ex.Message}");
                        }
                    }
                    else
                    {
                        string errorDetails = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Lỗi HTTP {StatusCode} khi đồng bộ: {Error}",
                            (int)response.StatusCode, errorDetails);

                        return (false, $"Lỗi HTTP {(int)response.StatusCode}: {errorDetails}");
                    }
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Đồng bộ HTTP bị hủy hoặc hết thời gian chờ");
                return (false, "Đồng bộ bị hủy hoặc hết thời gian chờ");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ HTTP: {Message}", ex.Message);
                return (false, $"Lỗi đồng bộ: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Kết quả đồng bộ từ server
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
}