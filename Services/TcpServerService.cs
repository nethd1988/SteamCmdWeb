using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private readonly SyncService _syncService;
        private readonly ProfileService _profileService;
        private readonly int _port = 61188;
        private TcpListener _listener;
        private bool _isRunning;

        public TcpServerService(
            ILogger<TcpServerService> logger,
            SyncService syncService,
            ProfileService profileService)
        {
            _logger = logger;
            _syncService = syncService;
            _profileService = profileService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                _isRunning = true;

                _logger.LogInformation("TCP Server đang lắng nghe trên cổng {Port}", _port);

                while (!stoppingToken.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        var tcpClient = await _listener.AcceptTcpClientAsync();
                        _logger.LogInformation("Nhận kết nối từ {ClientAddress}",
                            ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address);

                        // Xử lý mỗi kết nối trong một task riêng
                        _ = HandleClientAsync(tcpClient, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý kết nối TCP");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi khởi động TCP Server");
                _isRunning = false;
            }
            finally
            {
                _listener?.Stop();
                _logger.LogInformation("TCP Server đã dừng");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                using (client)
                {
                    var clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
                    string clientAddress = clientEndPoint.Address.ToString();

                    using (var stream = client.GetStream())
                    {
                        // Nhận và xử lý yêu cầu
                        byte[] lengthBuffer = new byte[4];
                        await stream.ReadAsync(lengthBuffer, 0, 4, cancellationToken);
                        int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                        if (messageLength <= 0 || messageLength > 1024 * 1024) // Giới hạn 1MB
                        {
                            _logger.LogWarning("Độ dài thông điệp không hợp lệ từ {ClientAddress}: {Length}",
                                clientAddress, messageLength);
                            return;
                        }

                        byte[] buffer = new byte[messageLength];
                        await stream.ReadAsync(buffer, 0, messageLength, cancellationToken);
                        string message = Encoding.UTF8.GetString(buffer);

                        if (message.StartsWith("AUTH:"))
                        {
                            // Xử lý yêu cầu xác thực và lệnh
                            await ProcessAuthenticatedRequestAsync(message, stream, clientAddress, cancellationToken);
                        }
                        else
                        {
                            _logger.LogWarning("Yêu cầu không xác thực từ {ClientAddress}", clientAddress);
                            await SendResponseAsync(stream, "ERROR:AUTHENTICATION_REQUIRED", cancellationToken);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Lỗi khi xử lý client TCP");
            }
        }

        private async Task ProcessAuthenticatedRequestAsync(
            string message, NetworkStream stream, string clientAddress, CancellationToken cancellationToken)
        {
            try
            {
                // Tách token xác thực và lệnh
                int authSeparator = message.IndexOf(' ');
                if (authSeparator <= 5) // "AUTH:" + ít nhất 1 ký tự
                {
                    await SendResponseAsync(stream, "ERROR:INVALID_AUTH_FORMAT", cancellationToken);
                    return;
                }

                string authToken = message.Substring(5, authSeparator - 5);
                string command = message.Substring(authSeparator + 1);

                // Kiểm tra token - đơn giản chỉ cần khớp với giá trị cố định
                if (authToken != "simple_auth_token")
                {
                    await SendResponseAsync(stream, "ERROR:INVALID_AUTH_TOKEN", cancellationToken);
                    return;
                }

                // Xử lý lệnh
                if (command.StartsWith("GET_PROFILES"))
                {
                    await HandleGetProfilesCommand(stream, cancellationToken);
                }
                else if (command.StartsWith("GET_PROFILE_DETAILS"))
                {
                    string profileName = command.Substring("GET_PROFILE_DETAILS".Length).Trim();
                    await HandleGetProfileDetailsCommand(stream, profileName, cancellationToken);
                }
                else if (command.StartsWith("SEND_PROFILES"))
                {
                    await HandleSendProfilesCommand(stream, clientAddress, cancellationToken);
                }
                else
                {
                    await SendResponseAsync(stream, "ERROR:UNKNOWN_COMMAND", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý yêu cầu xác thực từ {ClientAddress}", clientAddress);
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", cancellationToken);
            }
        }

        private async Task HandleGetProfilesCommand(NetworkStream stream, CancellationToken cancellationToken)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();

                if (profiles.Count == 0)
                {
                    await SendResponseAsync(stream, "NO_PROFILES", cancellationToken);
                    return;
                }

                // Trả về danh sách tên profile
                string response = string.Join(",", profiles.Select(p => p.Name));
                await SendResponseAsync(stream, response, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý lệnh GET_PROFILES");
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", cancellationToken);
            }
        }

        private async Task HandleGetProfileDetailsCommand(
            NetworkStream stream, string profileName, CancellationToken cancellationToken)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Name == profileName);

                if (profile == null)
                {
                    await SendResponseAsync(stream, "PROFILE_NOT_FOUND", cancellationToken);
                    return;
                }

                // Chuyển đổi về SteamCmdProfile để tương thích với client
                var responseProfile = new SteamCmdWebAPI.Models.SteamCmdProfile
                {
                    Name = profile.Name,
                    AppID = profile.AppID,
                    InstallDirectory = profile.InstallDirectory,
                    SteamUsername = profile.SteamUsername,
                    SteamPassword = profile.SteamPassword,
                    Arguments = profile.Arguments,
                    ValidateFiles = profile.ValidateFiles,
                    AutoRun = profile.AutoRun,
                    AnonymousLogin = profile.AnonymousLogin,
                    Status = "Ready"
                };

                string json = JsonSerializer.Serialize(responseProfile);
                await SendResponseAsync(stream, json, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý lệnh GET_PROFILE_DETAILS");
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", cancellationToken);
            }
        }

        private async Task HandleSendProfilesCommand(
            NetworkStream stream, string clientAddress, CancellationToken cancellationToken)
        {
            try
            {
                // Thông báo sẵn sàng nhận
                await SendResponseAsync(stream, "READY_TO_RECEIVE", cancellationToken);

                int processed = 0;
                int errors = 0;
                var clientProfiles = new List<ClientProfile>();

                // Đọc danh sách profile từ client
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Đọc độ dài
                    byte[] lengthBuffer = new byte[4];
                    await stream.ReadAsync(lengthBuffer, 0, 4, cancellationToken);
                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // Nếu độ dài là 0, kết thúc quá trình nhận
                    if (messageLength == 0)
                        break;

                    // Kiểm tra độ dài hợp lệ
                    if (messageLength < 0 || messageLength > 5 * 1024 * 1024) // Giới hạn 5MB
                    {
                        _logger.LogWarning("Độ dài profile không hợp lệ: {Length}", messageLength);
                        await SendResponseAsync(stream, "ERROR:INVALID_LENGTH", cancellationToken);
                        errors++;
                        continue;
                    }

                    // Đọc profile
                    byte[] buffer = new byte[messageLength];
                    await stream.ReadAsync(buffer, 0, messageLength, cancellationToken);
                    string json = Encoding.UTF8.GetString(buffer);

                    try
                    {
                        // Thử chuyển đổi từ SteamCmdProfile của client sang ClientProfile của server
                        var clientProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(json);
                        if (clientProfile != null)
                        {
                            // Chuyển đổi từ SteamCmdProfile sang ClientProfile
                            var serverProfile = new ClientProfile
                            {
                                Name = clientProfile.Name,
                                AppID = clientProfile.AppID,
                                InstallDirectory = clientProfile.InstallDirectory,
                                SteamUsername = clientProfile.SteamUsername,
                                SteamPassword = clientProfile.SteamPassword,
                                Arguments = clientProfile.Arguments,
                                ValidateFiles = clientProfile.ValidateFiles,
                                AutoRun = clientProfile.AutoRun,
                                AnonymousLogin = clientProfile.AnonymousLogin,
                                Status = "Ready",
                                StartTime = DateTime.Now,
                                StopTime = DateTime.Now,
                                LastRun = DateTime.UtcNow
                            };

                            clientProfiles.Add(serverProfile);
                            await SendResponseAsync(stream, $"SUCCESS:{clientProfile.Name}", cancellationToken);
                            processed++;
                        }
                        else
                        {
                            await SendResponseAsync(stream, "ERROR:INVALID_PROFILE", cancellationToken);
                            errors++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi phân tích profile JSON");
                        await SendResponseAsync(stream, $"ERROR:INVALID_JSON", cancellationToken);
                        errors++;
                    }
                }

                // Xử lý toàn bộ profile đã nhận
                if (clientProfiles.Count > 0)
                {
                    await ProcessAndSaveProfilesAsync(clientAddress, clientProfiles);
                }

                // Gửi kết quả cuối cùng
                await SendResponseAsync(stream, $"DONE:{processed}:{errors}", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý lệnh SEND_PROFILES");
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", cancellationToken);
            }
        }

        private async Task ProcessAndSaveProfilesAsync(string clientId, List<ClientProfile> clientProfiles)
        {
            try
            {
                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                int added = 0;
                int filtered = 0;

                _logger.LogInformation("Nhận được {Count} profiles từ client {ClientId}", clientProfiles.Count, clientId);

                foreach (var profile in clientProfiles)
                {
                    // Lọc profiles theo App ID
                    if (!existingAppIds.Contains(profile.AppID))
                    {
                        // Giữ nguyên tên đăng nhập và mật khẩu đã mã hóa
                        await _profileService.AddProfileAsync(profile);
                        existingAppIds.Add(profile.AppID);
                        added++;
                    }
                    else
                    {
                        filtered++;
                    }
                }

                var result = new SyncResult
                {
                    ClientId = clientId,
                    Success = true,
                    TotalProfiles = clientProfiles.Count,
                    NewProfilesAdded = added,
                    FilteredProfiles = filtered,
                    Message = $"Đồng bộ thành công. Thêm {added} profiles mới, bỏ qua {filtered} profiles trùng App ID.",
                    Timestamp = DateTime.Now
                };

                _logger.LogInformation("Đồng bộ từ {ClientId} hoàn tất: {Added} thêm mới, {Filtered} trùng lặp",
                    clientId, added, filtered);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý và lưu profiles từ {ClientId}", clientId);
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, string response, CancellationToken cancellationToken)
        {
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            byte[] lengthBytes = BitConverter.GetBytes(responseBytes.Length);

            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _isRunning = false;
            _listener?.Stop();
            await base.StopAsync(cancellationToken);
        }
    }

    // Định nghĩa namespace cho model của SteamCmdWebAPI client
    namespace SteamCmdWebAPI.Models
    {
        public class SteamCmdProfile
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string AppID { get; set; }
            public string InstallDirectory { get; set; }
            public string SteamUsername { get; set; }
            public string SteamPassword { get; set; }
            public string Arguments { get; set; }
            public bool ValidateFiles { get; set; }
            public bool AutoRun { get; set; }
            public bool AnonymousLogin { get; set; }
            public string Status { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime StopTime { get; set; }
            public int Pid { get; set; }
            public DateTime? LastRun { get; set; }
        }
    }
}