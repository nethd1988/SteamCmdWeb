using System;
using System.Collections.Generic;
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
        private readonly SyncService _syncService;
        private readonly int _port = 61188;
        private TcpListener _listener;
        private readonly DecryptionService _decryptionService;

        public TcpServerService(
            ILogger<TcpServerService> logger,
            SyncService syncService,
            DecryptionService decryptionService)
        {
            _logger = logger;
            _syncService = syncService;
            _decryptionService = decryptionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                _logger.LogInformation("TcpServerService[0] đã khởi động trên port {Port}", _port);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                        _logger.LogInformation("TcpServerService[0] nhận kết nối từ {ClientIp}", clientIp);

                        _ = Task.Run(() => HandleClientAsync(client, clientIp), stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "TcpServerService[0] lỗi khi xử lý yêu cầu kết nối");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TcpServerService[0] lỗi khi khởi động");
            }
            finally
            {
                _listener?.Stop();
                _logger.LogInformation("TcpServerService[0] đã dừng");
            }
        }

        private async Task HandleClientAsync(TcpClient client, string clientIp)
        {
            try
            {
                using (client)
                {
                    var stream = client.GetStream();

                    // Đọc độ dài thông điệp
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, lengthBuffer.Length);
                    if (bytesRead != 4)
                    {
                        _logger.LogWarning("TcpServerService[0] nhận dữ liệu không đầy đủ từ {ClientIp}", clientIp);
                        return;
                    }

                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                    _logger.LogInformation("TcpServerService[0] nhận dữ liệu dài: {Length} từ {ClientIp}", messageLength, clientIp);

                    // Đọc thông điệp
                    byte[] messageBuffer = new byte[messageLength];
                    bytesRead = await stream.ReadAsync(messageBuffer, 0, messageLength);
                    if (bytesRead != messageLength)
                    {
                        _logger.LogWarning("TcpServerService[0] dữ liệu đọc {BytesRead} không khớp với chiều dài {MessageLength}", bytesRead, messageLength);
                        return;
                    }

                    string message = Encoding.UTF8.GetString(messageBuffer);
                    _logger.LogInformation("TcpServerService[0] nhận thông điệp: {Message} từ {ClientIp}", message, clientIp);

                    // Xử lý lệnh
                    if (message.StartsWith("AUTH:"))
                    {
                        await ProcessAuthenticatedCommand(message, stream, clientIp);
                    }
                    else
                    {
                        _logger.LogWarning("TcpServerService[0] nhận lệnh không xác thực từ {ClientIp}: {Message}", clientIp, message);
                        await SendResponse(stream, "ERROR: Authentication required");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TcpServerService[0] lỗi khi xử lý client từ {ClientIp}", clientIp);
            }
        }

        private async Task ProcessAuthenticatedCommand(string command, NetworkStream stream, string clientIp)
        {
            // Phân tích lệnh
            string[] parts = command.Split(' ');
            if (parts.Length < 2)
            {
                _logger.LogWarning("TcpServerService[0] lệnh không hợp lệ từ {ClientIp}: {Command}", clientIp, command);
                await SendResponse(stream, "ERROR: Invalid command format");
                return;
            }

            string authPart = parts[0]; // AUTH:token
            string action = parts[1];   // GET_PROFILES, GET_PROFILE_DETAILS, SEND_PROFILE, SEND_PROFILES, etc.

            // Kiểm tra xác thực
            string[] authParts = authPart.Split(':');
            if (authParts.Length != 2 || authParts[0] != "AUTH")
            {
                _logger.LogWarning("TcpServerService[0] định dạng xác thực không hợp lệ từ {ClientIp}: {AuthPart}", clientIp, authPart);
                await SendResponse(stream, "ERROR: Invalid authentication format");
                return;
            }

            string token = authParts[1];
            if (token != "simple_auth_token")
            {
                _logger.LogWarning("TcpServerService[0] token không hợp lệ từ {ClientIp}: {Token}", clientIp, token);
                await SendResponse(stream, "ERROR: Invalid authentication token");
                return;
            }

            _logger.LogInformation("TcpServerService[0] nhận lệnh: {Action} từ {ClientIp}", action, clientIp);

            // Xử lý lệnh dựa trên action
            switch (action)
            {
                case "GET_PROFILES":
                    await ProcessGetProfilesCommand(stream, clientIp);
                    break;
                case "GET_PROFILE_DETAILS":
                    if (parts.Length < 3)
                    {
                        await SendResponse(stream, "ERROR: Missing profile name");
                    }
                    else
                    {
                        string profileName = parts[2];
                        await ProcessGetProfileDetailsCommand(profileName, stream, clientIp);
                    }
                    break;
                case "SEND_PROFILE":
                    await ProcessSendProfileCommand(stream, clientIp);
                    break;
                case "SEND_PROFILES":
                    await ProcessSendProfilesCommand(stream, clientIp);
                    break;
                default:
                    _logger.LogWarning("TcpServerService[0] lệnh không được hỗ trợ từ {ClientIp}: {Action}", clientIp, action);
                    await SendResponse(stream, $"ERROR: Unsupported command {action}");
                    break;
            }
        }

        private async Task ProcessGetProfilesCommand(NetworkStream stream, string clientIp)
        {
            try
            {
                // Lấy danh sách profiles từ SyncService
                var profiles = _syncService.GetAllProfiles();
                if (profiles == null || profiles.Count == 0)
                {
                    _logger.LogInformation("TcpServerService[0] không có profiles để gửi cho {ClientIp}", clientIp);
                    await SendResponse(stream, "NO_PROFILES");
                    return;
                }

                // Tạo danh sách tên profiles
                var profileNames = new List<string>();
                foreach (var profile in profiles)
                {
                    profileNames.Add(profile.Name);
                }

                // Gửi danh sách tên profiles
                string response = string.Join(",", profileNames);
                _logger.LogInformation("TcpServerService[0] gửi {Count} tên profiles tới {ClientIp}", profileNames.Count, clientIp);
                await SendResponse(stream, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TcpServerService[0] lỗi khi xử lý lệnh GET_PROFILES từ {ClientIp}", clientIp);
                await SendResponse(stream, "ERROR: " + ex.Message);
            }
        }

        private async Task ProcessGetProfileDetailsCommand(string profileName, NetworkStream stream, string clientIp)
        {
            try
            {
                // Lấy chi tiết profile từ SyncService
                var profile = _syncService.GetProfileByName(profileName);
                if (profile == null)
                {
                    _logger.LogWarning("TcpServerService[0] không tìm thấy profile {ProfileName} cho {ClientIp}", profileName, clientIp);
                    await SendResponse(stream, "PROFILE_NOT_FOUND");
                    return;
                }

                // Chuyển đổi từ ClientProfile sang SteamCmdProfile
                var steamCmdProfile = new SteamCmdWebAPI.Models.SteamCmdProfile
                {
                    Name = profile.Name,
                    AppID = profile.AppID,
                    InstallDirectory = profile.InstallDirectory,
                    Arguments = profile.Arguments,
                    ValidateFiles = profile.ValidateFiles,
                    AutoRun = profile.AutoRun,
                    AnonymousLogin = profile.AnonymousLogin,
                    Status = profile.Status,
                    StartTime = profile.StartTime,
                    StopTime = profile.StopTime,
                    LastRun = profile.LastRun
                };

                // Gửi chi tiết profile
                string response = JsonSerializer.Serialize(steamCmdProfile);
                _logger.LogInformation("TcpServerService[0] gửi chi tiết profile {ProfileName} tới {ClientIp}", profileName, clientIp);
                await SendResponse(stream, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TcpServerService[0] lỗi khi xử lý lệnh GET_PROFILE_DETAILS từ {ClientIp}", clientIp);
                await SendResponse(stream, "ERROR: " + ex.Message);
            }
        }

        private async Task ProcessSendProfileCommand(NetworkStream stream, string clientIp)
        {
            try
            {
                _logger.LogInformation("TcpServerService[0] chuẩn bị nhận profile từ {ClientIp}", clientIp);
                await SendResponse(stream, "READY_TO_RECEIVE");

                // Đọc độ dài profile
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await stream.ReadAsync(lengthBuffer, 0, lengthBuffer.Length);
                if (bytesRead != 4)
                {
                    _logger.LogWarning("TcpServerService[0] nhận dữ liệu không đầy đủ từ {ClientIp}", clientIp);
                    return;
                }

                int profileLength = BitConverter.ToInt32(lengthBuffer, 0);
                _logger.LogInformation("TcpServerService[0] nhận dữ liệu dài: {Length} từ {ClientIp}", profileLength, clientIp);

                // Đọc nội dung profile
                byte[] profileBuffer = new byte[profileLength];
                bytesRead = await stream.ReadAsync(profileBuffer, 0, profileLength);
                if (bytesRead != profileLength)
                {
                    _logger.LogWarning("TcpServerService[0] dữ liệu đọc {BytesRead} không khớp với chiều dài {ProfileLength}", bytesRead, profileLength);
                    await SendResponse(stream, "ERROR: Incomplete profile data");
                    return;
                }

                string profileJson = Encoding.UTF8.GetString(profileBuffer);
                _logger.LogInformation("TcpServerService[0] nhận dữ liệu JSON từ {ClientIp}: {Length} bytes", clientIp, profileJson.Length);

                // Chuyển đổi dữ liệu JSON thành đối tượng profile
                var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(profileJson);

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
                    LastRun = DateTime.UtcNow,
                    Pid = 0
                };

                // Thêm vào danh sách chờ xác nhận
                _logger.LogInformation("TcpServerService[0] đã thêm profile {ProfileName} từ {ClientIp} vào danh sách chờ", steamCmdProfile.Name, clientIp);
                _syncService.AddPendingProfile(clientProfile);

                await SendResponse(stream, $"SUCCESS: Profile {steamCmdProfile.Name} added to pending list");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TcpServerService[0] lỗi khi xử lý lệnh SEND_PROFILE từ {ClientIp}", clientIp);
                await SendResponse(stream, "ERROR: " + ex.Message);
            }
        }

        private async Task ProcessSendProfilesCommand(NetworkStream stream, string clientIp)
        {
            try
            {
                _logger.LogInformation("TcpServerService[0] chuẩn bị nhận nhiều profiles từ {ClientIp}", clientIp);
                await SendResponse(stream, "READY_TO_RECEIVE");

                int profileCount = 0;
                int errorCount = 0;

                while (true)
                {
                    // Đọc độ dài profile
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, lengthBuffer.Length);
                    if (bytesRead != 4)
                    {
                        _logger.LogWarning("TcpServerService[0] nhận dữ liệu không đầy đủ từ {ClientIp}", clientIp);
                        break;
                    }

                    int profileLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // Kiểm tra xem đã kết thúc chưa (độ dài 0 là marker kết thúc)
                    if (profileLength == 0)
                    {
                        _logger.LogInformation("TcpServerService[0] nhận marker kết thúc từ {ClientIp}", clientIp);
                        break;
                    }

                    // Đọc nội dung profile
                    byte[] profileBuffer = new byte[profileLength];
                    bytesRead = await stream.ReadAsync(profileBuffer, 0, profileLength);
                    if (bytesRead != profileLength)
                    {
                        _logger.LogWarning("TcpServerService[0] dữ liệu đọc {BytesRead} không khớp với chiều dài {ProfileLength}", bytesRead, profileLength);
                        await SendResponse(stream, "ERROR: Incomplete profile data");
                        errorCount++;
                        continue;
                    }

                    try
                    {
                        string profileJson = Encoding.UTF8.GetString(profileBuffer);

                        // Chuyển đổi dữ liệu JSON thành đối tượng profile
                        var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(profileJson);

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
                            LastRun = DateTime.UtcNow,
                            Pid = 0
                        };

                        // Thêm vào danh sách chờ xác nhận
                        _syncService.AddPendingProfile(clientProfile);
                        profileCount++;

                        await SendResponse(stream, $"SUCCESS: Profile {steamCmdProfile.Name} added to pending list");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "TcpServerService[0] lỗi khi xử lý profile thứ {Count} từ {ClientIp}", profileCount + 1, clientIp);
                        await SendResponse(stream, "ERROR: " + ex.Message);
                        errorCount++;
                    }
                }

                // Gửi kết quả tổng hợp
                await SendResponse(stream, $"DONE:{profileCount}:{errorCount}");
                _logger.LogInformation("TcpServerService[0] đã nhận {Count} profiles từ {ClientIp}, có {ErrorCount} lỗi", profileCount, clientIp, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TcpServerService[0] lỗi khi xử lý lệnh SEND_PROFILES từ {ClientIp}", clientIp);
                await SendResponse(stream, "ERROR: " + ex.Message);
            }
        }

        private async Task SendResponse(NetworkStream stream, string response)
        {
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            byte[] lengthBytes = BitConverter.GetBytes(responseBytes.Length);

            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
            await stream.FlushAsync();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("TcpServerService đang dừng...");
            _listener?.Stop();
            await base.StopAsync(cancellationToken);
        }
    }
}