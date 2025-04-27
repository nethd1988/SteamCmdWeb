using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System.Linq;

namespace SteamCmdWeb.Services
{
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private readonly ProfileService _profileService;
        private readonly SyncService _syncService;
        private readonly ClientTrackingService _clientTrackingService;
        private TcpListener _tcpListener;
        private readonly int _port = 61188;
        private bool _isRunning = false;

        public TcpServerService(
            ILogger<TcpServerService> logger,
            ProfileService profileService,
            SyncService syncService,
            ClientTrackingService clientTrackingService)
        {
            _logger = logger;
            _profileService = profileService;
            _syncService = syncService;
            _clientTrackingService = clientTrackingService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _port);
                _tcpListener.Start();
                _isRunning = true;

                _logger.LogInformation("TcpServerService đã khởi động trên cổng {Port}", _port);

                while (!stoppingToken.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync(stoppingToken);
                        _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi chấp nhận kết nối TCP");
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khởi động TcpServerService");
            }
            finally
            {
                _tcpListener?.Stop();
                _isRunning = false;
                _logger.LogInformation("TcpServerService đã dừng");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            string clientIp = "unknown";
            try
            {
                if (client?.Client?.RemoteEndPoint is IPEndPoint remoteEndPoint)
                {
                    clientIp = remoteEndPoint.Address.ToString();
                }
                _logger.LogInformation("Đã nhận kết nối từ {ClientIp}", clientIp);

                using (client)
                {
                    using NetworkStream stream = client.GetStream();
                    stream.ReadTimeout = 30000;
                    stream.WriteTimeout = 30000;

                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer.AsMemory(0, 4), stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Không đọc được thông tin độ dài từ client {ClientIp}", clientIp);
                        return;
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                    const int MaxDataLength = 5 * 1024 * 1024;
                    if (dataLength <= 0 || dataLength > MaxDataLength)
                    {
                        _logger.LogWarning("Độ dài dữ liệu không hợp lệ từ client {ClientIp}: {Length}", clientIp, dataLength);
                        await SendResponseAsync(stream, "ERROR:Invalid data length", stoppingToken);
                        return;
                    }

                    byte[] dataBuffer = new byte[dataLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < dataLength && !stoppingToken.IsCancellationRequested)
                    {
                        bytesRead = await stream.ReadAsync(dataBuffer.AsMemory(totalBytesRead, dataLength - totalBytesRead), stoppingToken);
                        if (bytesRead == 0)
                        {
                            _logger.LogWarning("Kết nối bị đóng sớm bởi client {ClientIp}", clientIp);
                            return;
                        }
                        totalBytesRead += bytesRead;
                    }

                    if (totalBytesRead < dataLength)
                    {
                        _logger.LogWarning("Dữ liệu không đầy đủ từ client {ClientIp}", clientIp);
                        return;
                    }

                    string command = Encoding.UTF8.GetString(dataBuffer, 0, totalBytesRead);
                    _logger.LogInformation("Nhận lệnh từ client {ClientIp}: {Command}", clientIp, command);

                    string clientId = ExtractClientIdFromCommand(command);
                    if (!string.IsNullOrEmpty(clientId) && clientId != "unknown")
                    {
                        _clientTrackingService.TrackClient(clientId, clientIp);
                    }

                    await ProcessCommandAsync(command, stream, clientIp, stoppingToken);
                }
            }
            catch (IOException ioEx) when (ioEx.InnerException is SocketException)
            {
                _logger.LogWarning("Lỗi Socket khi xử lý client {ClientIp}: {Message}", clientIp, ioEx.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Hoạt động xử lý client {ClientIp} đã bị hủy", clientIp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi không xác định khi xử lý client {ClientIp}", clientIp);
                try
                {
                    if (client != null && client.Connected)
                    {
                        using NetworkStream errorStream = client.GetStream();
                        await SendResponseAsync(errorStream, "ERROR:Internal server error", stoppingToken);
                    }
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "Không thể gửi thông báo lỗi cuối cùng tới client {ClientIp}", clientIp);
                }
            }
            finally
            {
                _logger.LogInformation("Đã đóng kết nối với client {ClientIp}", clientIp);
            }
        }

        private string ExtractClientIdFromCommand(string command)
        {
            try
            {
                if (command.Contains("CLIENT_ID:"))
                {
                    int startIndex = command.IndexOf("CLIENT_ID:") + 10;
                    int endIndex = command.IndexOf(" ", startIndex);
                    if (endIndex == -1) endIndex = command.Length;

                    if (endIndex > startIndex)
                    {
                        string clientId = command.Substring(startIndex, endIndex - startIndex);
                        _logger.LogDebug("Extracted ClientID: {ClientId}", clientId);
                        return clientId;
                    }
                }

                if (command.StartsWith("AUTH:"))
                {
                    string[] parts = command.Split(' ', 3);
                    if (parts.Length > 0 && parts[0].StartsWith("AUTH:"))
                    {
                        string authToken = parts[0].Substring(5);
                        return $"anonymous-{Math.Abs(authToken.GetHashCode())}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi trích xuất ClientID từ lệnh");
            }

            string randomId = $"unknown-{Guid.NewGuid().ToString().Substring(0, 8)}";
            _logger.LogDebug("Sử dụng ID ngẫu nhiên: {RandomId}", randomId);
            return randomId;
        }

        private async Task ProcessCommandAsync(string command, NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            if (!command.StartsWith("AUTH:"))
            {
                await SendResponseAsync(stream, "AUTHENTICATION_REQUIRED", stoppingToken);
                _logger.LogWarning("Client {ClientIp} không gửi xác thực", clientIp);
                return;
            }

            string[] authParts = command.Split(' ', 3);
            if (authParts.Length < 2)
            {
                await SendResponseAsync(stream, "INVALID_COMMAND", stoppingToken);
                _logger.LogWarning("Lệnh không hợp lệ từ client {ClientIp}", clientIp);
                return;
            }

            string authToken = authParts[0].Substring(5);
            string actualCommandPart = "";
            string clientId = "unknown";

            if (authParts.Length >= 3 && authParts[1].StartsWith("CLIENT_ID:"))
            {
                clientId = authParts[1].Substring(10);
                actualCommandPart = authParts[2];
            }
            else if (authParts.Length >= 2)
            {
                actualCommandPart = authParts[1];
            }
            else
            {
                await SendResponseAsync(stream, "INVALID_COMMAND", stoppingToken);
                _logger.LogWarning("Lệnh không hợp lệ từ client {ClientIp}", clientIp);
                return;
            }

            if (authToken != "simple_auth_token")
            {
                await SendResponseAsync(stream, "INVALID_TOKEN", stoppingToken);
                _logger.LogWarning("Token không hợp lệ từ client {ClientIp}", clientIp);
                return;
            }

            _logger.LogInformation("Client {ClientIp} xác thực thành công. Xử lý: {ActualCommand}", clientIp, actualCommandPart);

            if (actualCommandPart == "GET_PROFILES")
            {
                await HandleGetProfilesAsync(stream, stoppingToken);
            }
            else if (actualCommandPart.StartsWith("GET_PROFILE_DETAILS "))
            {
                string profileName = actualCommandPart.Substring("GET_PROFILE_DETAILS ".Length).Trim();
                await HandleGetProfileDetailsAsync(stream, profileName, stoppingToken);
            }
            else if (actualCommandPart == "SEND_PROFILE")
            {
                await HandleReceiveProfileAsync(stream, clientIp, stoppingToken);
            }
            else if (actualCommandPart == "SEND_PROFILES")
            {
                await HandleReceiveProfilesAsync(stream, clientIp, stoppingToken);
            }
            else if (actualCommandPart == "HEARTBEAT")
            {
                await HandleHeartbeatAsync(stream, clientId, clientIp, stoppingToken);
            }
            else
            {
                await SendResponseAsync(stream, "UNKNOWN_COMMAND", stoppingToken);
                _logger.LogWarning("Lệnh không xác định từ client {ClientIp}: {Command}", clientIp, actualCommandPart);
            }
        }

        private async Task<bool> ProcessSingleProfileAsync(string json, string clientIp)
        {
            ClientProfile clientProfile = null;

            try
            {
                // Thử các cách phân tích JSON khác nhau
                Exception lastException = null;

                // Cách 1: Sử dụng System.Text.Json
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    clientProfile = JsonSerializer.Deserialize<ClientProfile>(json, options);
                    if (clientProfile != null) goto ProfileDeserialized;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogDebug("Không thể phân tích bằng System.Text.Json: {Message}", ex.Message);
                }

                // Cách 2: Sử dụng Newtonsoft.Json
                try
                {
                    clientProfile = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientProfile>(json);
                    if (clientProfile != null) goto ProfileDeserialized;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogDebug("Không thể phân tích bằng Newtonsoft.Json: {Message}", ex.Message);
                }

                // Cách 3: Thử chuyển đổi từ SteamCmdProfile
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(json, options);
                    if (steamCmdProfile != null)
                    {
                        clientProfile = new ClientProfile
                        {
                            Name = steamCmdProfile.Name ?? "Unknown",
                            AppID = steamCmdProfile.AppID ?? "",
                            InstallDirectory = steamCmdProfile.InstallDirectory ?? "",
                            Arguments = steamCmdProfile.Arguments ?? "",
                            ValidateFiles = steamCmdProfile.ValidateFiles,
                            AutoRun = steamCmdProfile.AutoRun,
                            Status = "Pending",
                            SteamUsername = steamCmdProfile.SteamUsername,
                            SteamPassword = steamCmdProfile.SteamPassword,
                            StartTime = DateTime.Now,
                            StopTime = DateTime.Now,
                            LastRun = DateTime.UtcNow
                        };
                        goto ProfileDeserialized;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogDebug("Không thể chuyển từ SteamCmdProfile: {Message}", ex.Message);
                }

                // Nếu tất cả các cách đều thất bại
                if (lastException != null)
                {
                    _logger.LogError(lastException, "Không thể phân tích JSON từ client {ClientIp}", clientIp);
                }
                else
                {
                    _logger.LogError("Không thể phân tích JSON từ client {ClientIp} nhưng không có lỗi cụ thể", clientIp);
                }
                return false;

            ProfileDeserialized:
                if (clientProfile == null || string.IsNullOrWhiteSpace(clientProfile.Name))
                {
                    _logger.LogWarning("Profile không hợp lệ hoặc bị trống sau khi chuyển đổi");
                    return false;
                }

                // Thêm vào hàng đợi, không cố gắng giải mã
                _logger.LogInformation("Thêm profile vào danh sách chờ: Name={Name}", clientProfile.Name);
                _syncService.AddPendingProfile(clientProfile);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tổng thể khi xử lý dữ liệu JSON từ client {ClientIp}", clientIp);
                return false;
            }
        }

        private async Task HandleGetProfilesAsync(NetworkStream stream, CancellationToken stoppingToken)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                if (profiles == null || profiles.Count == 0)
                {
                    await SendResponseAsync(stream, "NO_PROFILES", stoppingToken);
                    return;
                }

                var profileNames = profiles.Select(p => p.Name).ToList();
                string result = string.Join(",", profileNames);
                await SendResponseAsync(stream, result, stoppingToken);
                _logger.LogInformation("Đã gửi {Count} tên profile", profileNames.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý GET_PROFILES");
                await SendResponseAsync(stream, "ERROR:Failed to get profiles", stoppingToken);
            }
        }

        private async Task HandleGetProfileDetailsAsync(NetworkStream stream, string profileName, CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                await SendResponseAsync(stream, "ERROR:Profile name cannot be empty", stoppingToken);
                _logger.LogWarning("Yêu cầu GET_PROFILE_DETAILS với tên profile rỗng");
                return;
            }

            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

                if (profile == null)
                {
                    await SendResponseAsync(stream, "PROFILE_NOT_FOUND", stoppingToken);
                    _logger.LogWarning("Không tìm thấy profile với tên: {ProfileName}", profileName);
                    return;
                }

                var decryptedProfile = await _profileService.GetDecryptedProfileByIdAsync(profile.Id);
                if (decryptedProfile == null)
                {
                    await SendResponseAsync(stream, "ERROR:Could not decrypt profile", stoppingToken);
                    return;
                }

                // Bảo đảm giá trị không null
                var profileToSend = new SteamCmdWebAPI.Models.SteamCmdProfile
                {
                    Id = decryptedProfile.Id,
                    Name = decryptedProfile.Name ?? "",
                    AppID = decryptedProfile.AppID ?? "",
                    InstallDirectory = decryptedProfile.InstallDirectory ?? "",
                    SteamUsername = decryptedProfile.SteamUsername ?? "",
                    SteamPassword = decryptedProfile.SteamPassword ?? "",
                    Arguments = decryptedProfile.Arguments ?? "",
                    ValidateFiles = decryptedProfile.ValidateFiles,
                    AutoRun = decryptedProfile.AutoRun,
                    Status = decryptedProfile.Status ?? "Ready",
                    LastRun = decryptedProfile.LastRun
                };

                string json = JsonSerializer.Serialize(profileToSend);
                await SendResponseAsync(stream, json, stoppingToken);
                _logger.LogInformation("Đã gửi thông tin chi tiết cho profile {ProfileName}", profileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý GET_PROFILE_DETAILS cho {ProfileName}", profileName);
                await SendResponseAsync(stream, "ERROR:Internal server error", stoppingToken);
            }
        }

        private async Task HandleReceiveProfileAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);

                byte[] lengthBuffer = new byte[4];
                int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Không đọc được thông tin độ dài từ client {ClientIp}", clientIp);
                    return;
                }

                int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (dataLength <= 0 || dataLength > 5 * 1024 * 1024)
                {
                    _logger.LogWarning("Độ dài dữ liệu không hợp lệ từ client {ClientIp}: {Length}", clientIp, dataLength);
                    await SendResponseAsync(stream, "ERROR:Invalid data length", stoppingToken);
                    return;
                }

                byte[] dataBuffer = new byte[dataLength];
                int totalBytesRead = 0;
                while (totalBytesRead < dataLength && !stoppingToken.IsCancellationRequested)
                {
                    bytesRead = await stream.ReadAsync(dataBuffer, totalBytesRead, dataLength - totalBytesRead, stoppingToken);
                    if (bytesRead == 0) break;
                    totalBytesRead += bytesRead;
                }

                if (totalBytesRead < dataLength)
                {
                    _logger.LogWarning("Dữ liệu không đầy đủ từ client {ClientIp}", clientIp);
                    await SendResponseAsync(stream, "ERROR:Incomplete data", stoppingToken);
                    return;
                }

                string json = Encoding.UTF8.GetString(dataBuffer, 0, totalBytesRead);
                _logger.LogDebug("Nhận dữ liệu JSON: {Json}", json);

                bool success = await ProcessSingleProfileAsync(json, clientIp);
                if (success)
                {
                    await SendResponseAsync(stream, "SUCCESS:Profile added", stoppingToken);
                }
                else
                {
                    await SendResponseAsync(stream, "ERROR:Invalid profile data", stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profile từ client {ClientIp}", clientIp);
                await SendResponseAsync(stream, "ERROR:Internal server error", stoppingToken);
            }
        }

        private async Task HandleReceiveProfilesAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);

                int processedCount = 0;
                int errorCount = 0;

                while (!stoppingToken.IsCancellationRequested)
                {
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Không đọc được thông tin độ dài từ client {ClientIp}", clientIp);
                        break;
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0);

                    if (dataLength == 0)
                    {
                        _logger.LogInformation("Nhận được marker kết thúc từ client {ClientIp}", clientIp);
                        break;
                    }

                    if (dataLength < 0 || dataLength > 5 * 1024 * 1024)
                    {
                        _logger.LogWarning("Độ dài profile không hợp lệ: {Length}", dataLength);
                        errorCount++;
                        continue;
                    }

                    byte[] dataBuffer = new byte[dataLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < dataLength && !stoppingToken.IsCancellationRequested)
                    {
                        bytesRead = await stream.ReadAsync(dataBuffer, totalBytesRead, dataLength - totalBytesRead, stoppingToken);
                        if (bytesRead == 0) break;
                        totalBytesRead += bytesRead;
                    }

                    if (totalBytesRead < dataLength)
                    {
                        _logger.LogWarning("Dữ liệu không đầy đủ từ client {ClientIp}", clientIp);
                        errorCount++;
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(dataBuffer, 0, totalBytesRead);
                    bool success = await ProcessSingleProfileAsync(json, clientIp);

                    if (success)
                    {
                        processedCount++;
                    }
                    else
                    {
                        errorCount++;
                    }
                }

                await SendResponseAsync(stream, $"DONE:{processedCount}:{errorCount}", stoppingToken);
                _logger.LogInformation("Hoàn tất nhận profiles từ client {ClientIp}. Thành công: {SuccessCount}, Lỗi: {ErrorCount}",
                    clientIp, processedCount, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profiles từ client {ClientIp}", clientIp);
                await SendResponseAsync(stream, "ERROR:Internal server error", stoppingToken);
            }
        }

        private async Task HandleHeartbeatAsync(NetworkStream stream, string clientId, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogDebug("Nhận heartbeat từ client {ClientId} ({ClientIp})", clientId, clientIp);

                // Cập nhật trạng thái client là online
                _clientTrackingService.TrackClient(clientId, clientIp);

                // Gửi phản hồi ACK
                await SendResponseAsync(stream, "ACK", stoppingToken);

                _logger.LogDebug("Đã gửi phản hồi heartbeat cho client {ClientId}", clientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý heartbeat từ client {ClientId}", clientId);
                await SendResponseAsync(stream, "ERROR:Internal server error", stoppingToken);
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, string response, CancellationToken stoppingToken)
        {
            try
            {
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                byte[] lengthBytes = BitConverter.GetBytes(responseBytes.Length);

                await stream.WriteAsync(lengthBytes, 0, 4, stoppingToken);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, stoppingToken);
                await stream.FlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể gửi phản hồi: {Response}", response);
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _isRunning = false;
            _tcpListener?.Stop();
            _logger.LogInformation("Yêu cầu dừng TcpServerService...");
            await base.StopAsync(stoppingToken);
            _logger.LogInformation("TcpServerService đã dừng hoàn toàn");
        }
    }
}