using System;
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
using SteamCmdWeb.Services;

namespace SteamCmdWeb.Services
{
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;
        private readonly SyncService _syncService;
        private TcpListener _listener;
        private const int ServerPort = 61188;
        private const string AuthToken = "simple_auth_token";
        private bool _isRunning = true;

        public TcpServerService(
            ILogger<TcpServerService> logger,
            ProfileService profileService,
            DecryptionService decryptionService,
            SyncService syncService)
        {
            _logger = logger;
            _profileService = profileService;
            _decryptionService = decryptionService;
            _syncService = syncService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Khởi tạo TcpListener trên port 61188
                _listener = new TcpListener(IPAddress.Any, ServerPort);
                _listener.Start();
                _logger.LogInformation("Server TCP đang lắng nghe trên port {Port}", ServerPort);

                while (!stoppingToken.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        // Chờ kết nối từ client với timeout
                        using var timeoutCts = new CancellationTokenSource();
                        var connectTask = _listener.AcceptTcpClientAsync();
                        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, stoppingToken);

                        // Đợi kết nối với timeout 5 giây
                        var timeoutTask = Task.Delay(5000, combinedCts.Token);
                        var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            continue;
                        }

                        var client = await connectTask;
                        string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                        _logger.LogInformation("Nhận kết nối từ {ClientIp}", clientIp);

                        // Xử lý client trên thread riêng để không chặn main thread
                        _ = Task.Run(() => HandleClientAsync(client, clientIp, stoppingToken), stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Lỗi khi chờ kết nối client");
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi không xử lý được trong TCP Server Service");
            }
            finally
            {
                _listener?.Stop();
                _logger.LogInformation("Server TCP đã dừng");
            }
        }

        private async Task HandleClientAsync(TcpClient client, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                using (client)
                {
                    NetworkStream stream = client.GetStream();
                    stream.ReadTimeout = 30000; // 30 giây
                    stream.WriteTimeout = 30000; // 30 giây

                    // Đọc dữ liệu từ client
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Nhận dữ liệu không đầy đủ từ {ClientIp}", clientIp);
                        return;
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (dataLength <= 0 || dataLength > 1024 * 1024) // Giới hạn 1MB
                    {
                        _logger.LogWarning("Độ dài dữ liệu không hợp lý từ {ClientIp}: {Length}", clientIp, dataLength);
                        return;
                    }

                    byte[] dataBuffer = new byte[dataLength];
                    bytesRead = await stream.ReadAsync(dataBuffer, 0, dataLength, stoppingToken);
                    if (bytesRead < dataLength)
                    {
                        _logger.LogWarning("Nhận dữ liệu không đầy đủ từ {ClientIp}", clientIp);
                        return;
                    }

                    string command = Encoding.UTF8.GetString(dataBuffer);
                    _logger.LogInformation("Nhận lệnh từ {ClientIp}: {Command}", clientIp, command);

                    // Kiểm tra lệnh và xử lý
                    if (command.StartsWith("AUTH:"))
                    {
                        string[] parts = command.Split(' ', 2);
                        string authPart = parts[0];
                        string commandPart = parts.Length > 1 ? parts[1] : string.Empty;

                        string[] authParts = authPart.Split(':', 2);
                        string token = authParts.Length > 1 ? authParts[1] : string.Empty;

                        if (token != AuthToken)
                        {
                            _logger.LogWarning("Token không hợp lệ từ {ClientIp}: {Token}", clientIp, token);
                            await SendResponseAsync(stream, "ERROR:INVALID_TOKEN", stoppingToken);
                            return;
                        }

                        await ProcessCommandAsync(stream, commandPart, clientIp, stoppingToken);
                    }
                    else
                    {
                        _logger.LogWarning("Lệnh không hợp lệ từ {ClientIp}: {Command}", clientIp, command);
                        await SendResponseAsync(stream, "ERROR:UNKNOWN_COMMAND", stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý client {ClientIp}: {Message}", clientIp, ex.Message);
            }
        }

        private async Task ProcessCommandAsync(NetworkStream stream, string command, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Xử lý lệnh {Command} từ {ClientIp}", command, clientIp);

                switch (command)
                {
                    case "GET_PROFILES":
                        await HandleGetProfilesAsync(stream, clientIp, stoppingToken);
                        break;

                    case var cmd when cmd.StartsWith("GET_PROFILE_DETAILS "):
                        string profileName = cmd.Substring("GET_PROFILE_DETAILS ".Length).Trim();
                        await HandleGetProfileDetailsAsync(stream, profileName, clientIp, stoppingToken);
                        break;

                    case "SEND_PROFILE":
                        await HandleSendProfileAsync(stream, clientIp, stoppingToken);
                        break;

                    case "SEND_PROFILES":
                        await HandleSendProfilesAsync(stream, clientIp, stoppingToken);
                        break;

                    default:
                        _logger.LogWarning("Lệnh không xác định từ {ClientIp}: {Command}", clientIp, command);
                        await SendResponseAsync(stream, "ERROR:UNKNOWN_COMMAND", stoppingToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý lệnh {Command} từ {ClientIp}: {Message}", command, clientIp, ex.Message);
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
            }
        }

        private async Task HandleGetProfilesAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            var profiles = await _profileService.GetAllProfilesAsync();
            if (profiles.Count == 0)
            {
                await SendResponseAsync(stream, "NO_PROFILES", stoppingToken);
                return;
            }

            var profileNames = profiles.Select(p => p.Name);
            string response = string.Join(",", profileNames);
            await SendResponseAsync(stream, response, stoppingToken);
            _logger.LogInformation("Đã gửi danh sách {Count} profiles cho {ClientIp}", profiles.Count, clientIp);
        }

        private async Task HandleGetProfileDetailsAsync(NetworkStream stream, string profileName, string clientIp, CancellationToken stoppingToken)
        {
            if (string.IsNullOrEmpty(profileName))
            {
                await SendResponseAsync(stream, "ERROR:PROFILE_NAME_REQUIRED", stoppingToken);
                return;
            }

            var profiles = await _profileService.GetAllProfilesAsync();
            var profile = profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                await SendResponseAsync(stream, "PROFILE_NOT_FOUND", stoppingToken);
                return;
            }

            // Chuyển đổi ClientProfile sang SteamCmdProfile để đảm bảo tương thích
            var steamCmdProfile = new SteamCmdWebAPI.Models.SteamCmdProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                AppID = profile.AppID,
                InstallDirectory = profile.InstallDirectory,
                SteamUsername = profile.SteamUsername,
                SteamPassword = profile.SteamPassword,
                Arguments = profile.Arguments,
                ValidateFiles = profile.ValidateFiles,
                AutoRun = profile.AutoRun,
                AnonymousLogin = profile.AnonymousLogin,
                Status = profile.Status,
                StartTime = profile.StartTime,
                StopTime = profile.StopTime,
                Pid = profile.Pid,
                LastRun = profile.LastRun
            };

            string json = JsonSerializer.Serialize(steamCmdProfile);
            await SendResponseAsync(stream, json, stoppingToken);
            _logger.LogInformation("Đã gửi thông tin profile {ProfileName} cho {ClientIp}", profileName, clientIp);
        }

        private async Task HandleSendProfileAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                // Gửi phản hồi sẵn sàng nhận
                await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);
                _logger.LogInformation("Đã gửi READY_TO_RECEIVE cho {ClientIp}", clientIp);

                // Đọc độ dài profile
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Nhận dữ liệu độ dài không đầy đủ từ {ClientIp}", clientIp);
                    await SendResponseAsync(stream, "ERROR:INVALID_LENGTH", stoppingToken);
                    return;
                }

                int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                _logger.LogInformation("Nhận dữ liệu độ dài: {Length} từ {ClientIp}", dataLength, clientIp);

                if (dataLength <= 0 || dataLength > 5 * 1024 * 1024) // Giới hạn 5MB
                {
                    _logger.LogWarning("Độ dài dữ liệu profile không hợp lý từ {ClientIp}: {Length}", clientIp, dataLength);
                    await SendResponseAsync(stream, "ERROR:INVALID_DATA_LENGTH", stoppingToken);
                    return;
                }

                // Đọc nội dung profile
                byte[] dataBuffer = new byte[dataLength];
                bytesRead = await stream.ReadAsync(dataBuffer, 0, dataLength, stoppingToken);
                if (bytesRead < dataLength)
                {
                    _logger.LogWarning("Nhận dữ liệu profile không đầy đủ từ {ClientIp}: nhận {Received}/{Expected} bytes",
                        clientIp, bytesRead, dataLength);
                    await SendResponseAsync(stream, "ERROR:INCOMPLETE_DATA", stoppingToken);
                    return;
                }

                string json = Encoding.UTF8.GetString(dataBuffer);
                _logger.LogInformation("Nhận dữ liệu JSON từ {ClientIp}: {JsonLength} bytes", clientIp, json.Length);

                // Deserialize JSON
                SteamCmdWebAPI.Models.SteamCmdProfile steamCmdProfile;
                try
                {
                    steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi chuyển đổi dữ liệu JSON từ {ClientIp}: {Message}", clientIp, ex.Message);
                    await SendResponseAsync(stream, "ERROR:INVALID_JSON", stoppingToken);
                    return;
                }

                if (steamCmdProfile == null)
                {
                    _logger.LogWarning("Profile không hợp lệ từ {ClientIp}", clientIp);
                    await SendResponseAsync(stream, "ERROR:INVALID_PROFILE", stoppingToken);
                    return;
                }

                // Chuyển đổi sang ClientProfile
                var clientProfile = new ClientProfile
                {
                    Name = steamCmdProfile.Name,
                    AppID = steamCmdProfile.AppID,
                    InstallDirectory = steamCmdProfile.InstallDirectory,
                    SteamUsername = steamCmdProfile.SteamUsername,
                    SteamPassword = steamCmdProfile.SteamPassword,
                    Arguments = steamCmdProfile.Arguments,
                    ValidateFiles = steamCmdProfile.ValidateFiles,
                    AutoRun = steamCmdProfile.AutoRun,
                    AnonymousLogin = steamCmdProfile.AnonymousLogin,
                    Status = "Ready",
                    StartTime = DateTime.Now,
                    StopTime = DateTime.Now,
                    LastRun = DateTime.UtcNow
                };

                // Thêm vào danh sách chờ xác nhận
                _syncService.GetPendingProfiles().Add(clientProfile);
                _logger.LogInformation("Đã thêm profile {ProfileName} từ {ClientIp} vào danh sách chờ",
                    clientProfile.Name, clientIp);

                await SendResponseAsync(stream, $"SUCCESS:ADDED_TO_PENDING", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý nhận profile từ {ClientIp}: {Message}", clientIp, ex.Message);
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
            }
        }

        private async Task HandleSendProfilesAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                int processedCount = 0;
                int errorCount = 0;

                // Gửi phản hồi sẵn sàng nhận
                await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);
                _logger.LogInformation("Đã gửi READY_TO_RECEIVE cho {ClientIp} để nhận nhiều profiles", clientIp);

                while (!stoppingToken.IsCancellationRequested)
                {
                    // Đọc độ dài profile
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Nhận dữ liệu độ dài không đầy đủ từ {ClientIp}", clientIp);
                        errorCount++;
                        break;
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                    _logger.LogInformation("Nhận dữ liệu độ dài: {Length} từ {ClientIp}", dataLength, clientIp);

                    if (dataLength == 0) // Marker kết thúc
                    {
                        _logger.LogInformation("Đã nhận marker kết thúc từ {ClientIp}", clientIp);
                        break;
                    }

                    if (dataLength < 0 || dataLength > 5 * 1024 * 1024) // Giới hạn 5MB
                    {
                        _logger.LogWarning("Độ dài dữ liệu profile không hợp lý từ {ClientIp}: {Length}", clientIp, dataLength);
                        errorCount++;
                        await SendResponseAsync(stream, "ERROR:INVALID_DATA_LENGTH", stoppingToken);
                        continue;
                    }

                    // Đọc nội dung profile
                    byte[] dataBuffer = new byte[dataLength];
                    bytesRead = await stream.ReadAsync(dataBuffer, 0, dataLength, stoppingToken);
                    if (bytesRead < dataLength)
                    {
                        _logger.LogWarning("Nhận dữ liệu profile không đầy đủ từ {ClientIp}: nhận {Received}/{Expected} bytes",
                            clientIp, bytesRead, dataLength);
                        errorCount++;
                        await SendResponseAsync(stream, "ERROR:INCOMPLETE_DATA", stoppingToken);
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(dataBuffer);
                    _logger.LogInformation("Nhận dữ liệu JSON từ {ClientIp}: {JsonLength} bytes", clientIp, json.Length);

                    try
                    {
                        var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(json);
                        if (steamCmdProfile == null)
                        {
                            _logger.LogWarning("Profile không hợp lệ từ {ClientIp}", clientIp);
                            errorCount++;
                            await SendResponseAsync(stream, "ERROR:INVALID_PROFILE", stoppingToken);
                            continue;
                        }

                        // Chuyển đổi sang ClientProfile
                        var clientProfile = new ClientProfile
                        {
                            Name = steamCmdProfile.Name,
                            AppID = steamCmdProfile.AppID,
                            InstallDirectory = steamCmdProfile.InstallDirectory,
                            SteamUsername = steamCmdProfile.SteamUsername,
                            SteamPassword = steamCmdProfile.SteamPassword,
                            Arguments = steamCmdProfile.Arguments,
                            ValidateFiles = steamCmdProfile.ValidateFiles,
                            AutoRun = steamCmdProfile.AutoRun,
                            AnonymousLogin = steamCmdProfile.AnonymousLogin,
                            Status = "Ready",
                            StartTime = DateTime.Now,
                            StopTime = DateTime.Now,
                            LastRun = DateTime.UtcNow
                        };

                        // Thêm vào danh sách chờ xác nhận
                        _syncService.GetPendingProfiles().Add(clientProfile);
                        _logger.LogInformation("Đã thêm profile {ProfileName} từ {ClientIp} vào danh sách chờ",
                            clientProfile.Name, clientIp);

                        processedCount++;
                        await SendResponseAsync(stream, $"SUCCESS:ADDED_TO_PENDING", stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile từ {ClientIp}: {Message}", clientIp, ex.Message);
                        errorCount++;
                        await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
                    }
                }

                // Gửi thống kê kết quả
                await SendResponseAsync(stream, $"DONE:{processedCount}:{errorCount}", stoppingToken);
                _logger.LogInformation("Đã hoàn thành nhận profiles từ {ClientIp}: {Processed} thêm vào danh sách chờ, {Errors} lỗi",
                    clientIp, processedCount, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý nhận nhiều profiles từ {ClientIp}: {Message}", clientIp, ex.Message);
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, string response, CancellationToken stoppingToken)
        {
            try
            {
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                byte[] lengthBytes = BitConverter.GetBytes(responseBytes.Length);
                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, stoppingToken);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length, stoppingToken);
                await stream.FlushAsync(stoppingToken);
                _logger.LogInformation("Đã gửi phản hồi: {ResponseLength} bytes", responseBytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi phản hồi: {Message}", ex.Message);
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _isRunning = false;
            _listener?.Stop();
            await base.StopAsync(stoppingToken);
        }
    }
}