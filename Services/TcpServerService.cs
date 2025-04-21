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

namespace SteamCmdWeb.Services
{
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private readonly ProfileService _profileService;
        private readonly SyncService _syncService;
        private TcpListener _tcpListener;
        private readonly int _port = 61188;
        private bool _isRunning = false;

        public TcpServerService(
            ILogger<TcpServerService> logger,
            ProfileService profileService,
            SyncService syncService)
        {
            _logger = logger;
            _profileService = profileService;
            _syncService = syncService;
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
                        // Xử lý client trong một task riêng để không block main thread
                        _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi chấp nhận kết nối TCP");
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
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            _logger.LogInformation("Đã nhận kết nối từ {ClientIp}", clientIp);

            using (client)
            {
                using NetworkStream stream = client.GetStream();
                try
                {
                    // Đọc dữ liệu từ client
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Không đọc được thông tin độ dài từ client {ClientIp}", clientIp);
                        return;
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (dataLength <= 0 || dataLength > 1024 * 1024) // Giới hạn 1MB
                    {
                        _logger.LogWarning("Độ dài dữ liệu không hợp lệ từ client {ClientIp}: {Length}", clientIp, dataLength);
                        return;
                    }

                    byte[] dataBuffer = new byte[dataLength];
                    bytesRead = await stream.ReadAsync(dataBuffer, 0, dataLength, stoppingToken);
                    if (bytesRead < dataLength)
                    {
                        _logger.LogWarning("Dữ liệu không đầy đủ từ client {ClientIp}", clientIp);
                        return;
                    }

                    string command = Encoding.UTF8.GetString(dataBuffer, 0, bytesRead);
                    _logger.LogInformation("Nhận lệnh từ client {ClientIp}: {Command}", clientIp, command);

                    // Xử lý lệnh
                    await ProcessCommandAsync(command, stream, clientIp, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi xử lý client {ClientIp}", clientIp);
                }
            }
        }

        private async Task ProcessCommandAsync(string command, NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            // Kiểm tra xác thực (đơn giản)
            if (!command.StartsWith("AUTH:"))
            {
                await SendResponseAsync(stream, "AUTHENTICATION_REQUIRED", stoppingToken);
                _logger.LogWarning("Client {ClientIp} không gửi xác thực", clientIp);
                return;
            }

            string[] parts = command.Split(' ', 2);
            if (parts.Length < 2)
            {
                await SendResponseAsync(stream, "INVALID_COMMAND", stoppingToken);
                _logger.LogWarning("Lệnh không hợp lệ từ client {ClientIp}: {Command}", clientIp, command);
                return;
            }

            string authToken = parts[0].Substring(5); // "AUTH:token" -> "token"
            string actualCommand = parts[1];

            // Kiểm tra token (đơn giản)
            // Trong môi trường thực tế, nên sử dụng xác thực mạnh hơn
            if (authToken != "simple_auth_token")
            {
                await SendResponseAsync(stream, "INVALID_TOKEN", stoppingToken);
                _logger.LogWarning("Token không hợp lệ từ client {ClientIp}", clientIp);
                return;
            }

            // Xử lý các lệnh cụ thể
            if (actualCommand == "GET_PROFILES")
            {
                await HandleGetProfilesAsync(stream, stoppingToken);
            }
            else if (actualCommand.StartsWith("GET_PROFILE_DETAILS "))
            {
                string profileName = actualCommand.Substring("GET_PROFILE_DETAILS ".Length);
                await HandleGetProfileDetailsAsync(stream, profileName, stoppingToken);
            }
            else if (actualCommand == "SEND_PROFILE")
            {
                await HandleReceiveProfileAsync(stream, clientIp, stoppingToken);
            }
            else if (actualCommand == "SEND_PROFILES")
            {
                await HandleReceiveProfilesAsync(stream, clientIp, stoppingToken);
            }
            else
            {
                await SendResponseAsync(stream, "UNKNOWN_COMMAND", stoppingToken);
                _logger.LogWarning("Lệnh không xác định từ client {ClientIp}: {Command}", clientIp, actualCommand);
            }
        }

        private async Task HandleGetProfilesAsync(NetworkStream stream, CancellationToken stoppingToken)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                if (profiles.Count == 0)
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
                await SendResponseAsync(stream, "ERROR", stoppingToken);
            }
        }

        private async Task HandleGetProfileDetailsAsync(NetworkStream stream, string profileName, CancellationToken stoppingToken)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Name == profileName);

                if (profile == null)
                {
                    await SendResponseAsync(stream, "PROFILE_NOT_FOUND", stoppingToken);
                    return;
                }

                // Lấy profile có giải mã thông tin đăng nhập
                var decryptedProfile = await _profileService.GetDecryptedProfileByIdAsync(profile.Id);

                // Chuyển đổi từ ClientProfile sang SteamCmdProfile để gửi về client
                var steamCmdProfile = new SteamCmdWebAPI.Models.SteamCmdProfile
                {
                    Id = decryptedProfile.Id,
                    Name = decryptedProfile.Name,
                    AppID = decryptedProfile.AppID,
                    InstallDirectory = decryptedProfile.InstallDirectory,
                    Arguments = decryptedProfile.Arguments,
                    ValidateFiles = decryptedProfile.ValidateFiles,
                    AutoRun = decryptedProfile.AutoRun,
                    AnonymousLogin = decryptedProfile.AnonymousLogin,
                    Status = decryptedProfile.Status,
                    StartTime = decryptedProfile.StartTime,
                    StopTime = decryptedProfile.StopTime,
                    Pid = decryptedProfile.Pid,
                    LastRun = decryptedProfile.LastRun,
                    // Gửi cả thông tin đăng nhập đã giải mã
                    SteamUsername = decryptedProfile.AnonymousLogin ? "" : decryptedProfile.SteamUsername,
                    SteamPassword = decryptedProfile.AnonymousLogin ? "" : decryptedProfile.SteamPassword
                };

                string json = JsonSerializer.Serialize(steamCmdProfile);
                await SendResponseAsync(stream, json, stoppingToken);
                _logger.LogInformation("Đã gửi thông tin chi tiết cho profile {ProfileName}", profileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý GET_PROFILE_DETAILS cho {ProfileName}", profileName);
                await SendResponseAsync(stream, "ERROR", stoppingToken);
            }
        }

        private async Task HandleReceiveProfileAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);

                // Đọc độ dài profile
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Không đọc được thông tin độ dài profile từ client {ClientIp}", clientIp);
                    return;
                }

                int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (dataLength <= 0 || dataLength > 5 * 1024 * 1024) // Giới hạn 5MB
                {
                    _logger.LogWarning("Độ dài profile không hợp lệ từ client {ClientIp}: {Length}", clientIp, dataLength);
                    return;
                }

                byte[] dataBuffer = new byte[dataLength];
                bytesRead = await stream.ReadAsync(dataBuffer, 0, dataLength, stoppingToken);
                if (bytesRead < dataLength)
                {
                    _logger.LogWarning("Dữ liệu profile không đầy đủ từ client {ClientIp}", clientIp);
                    return;
                }

                string json = Encoding.UTF8.GetString(dataBuffer, 0, bytesRead);
                _logger.LogDebug("Nhận JSON profile: {Json}", json); // Ghi lại JSON nhận được
                var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(json);

                if (steamCmdProfile == null)
                {
                    await SendResponseAsync(stream, "ERROR:Invalid profile data", stoppingToken);
                    return;
                }

                // Log thông tin xác nhận nhận được
                _logger.LogInformation("Nhận profile: Name={Name}, AppID={AppID}, Username={Username}, Password={Password}, Anonymous={Anonymous}",
                    steamCmdProfile.Name, steamCmdProfile.AppID,
                    steamCmdProfile.SteamUsername, steamCmdProfile.SteamPassword,
                    steamCmdProfile.AnonymousLogin);

                // Chuyển đổi thành ClientProfile và thêm vào danh sách chờ
                var clientProfile = new ClientProfile
                {
                    Name = steamCmdProfile.Name ?? "Unnamed Profile",
                    AppID = steamCmdProfile.AppID ?? "",
                    InstallDirectory = steamCmdProfile.InstallDirectory ?? "",
                    Arguments = steamCmdProfile.Arguments ?? "",
                    ValidateFiles = steamCmdProfile.ValidateFiles,
                    AutoRun = steamCmdProfile.AutoRun,
                    AnonymousLogin = steamCmdProfile.AnonymousLogin,
                    Status = "Ready",
                    StartTime = DateTime.Now,
                    StopTime = DateTime.Now,
                    LastRun = DateTime.UtcNow
                };

                // Đặt tài khoản và mật khẩu trực tiếp
                clientProfile.SteamUsername = steamCmdProfile.SteamUsername ?? "";
                clientProfile.SteamPassword = steamCmdProfile.SteamPassword ?? "";

                // Thêm vào danh sách chờ xác nhận
                _syncService.AddPendingProfile(clientProfile);

                await SendResponseAsync(stream, $"SUCCESS:Added profile {clientProfile.Name} to pending list", stoppingToken);
                _logger.LogInformation("Đã nhận profile {ProfileName} từ client {ClientIp} với thông tin đăng nhập: {Username}/{Password}",
                    clientProfile.Name, clientIp, clientProfile.SteamUsername, clientProfile.SteamPassword);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profile từ client {ClientIp}", clientIp);
                await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
            }
        }

        private async Task HandleReceiveProfilesAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                await SendResponseAsync(stream, "READY_TO_RECEIVE", stoppingToken);

                int processedCount = 0;
                int errorCount = 0;

                // Nhận các profiles cho đến khi nhận được marker kết thúc
                while (true)
                {
                    // Đọc độ dài profile
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Không đọc được thông tin độ dài profile từ client {ClientIp}", clientIp);
                        break;
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (dataLength == 0)
                    {
                        // Đã nhận tất cả profiles
                        _logger.LogInformation("Nhận được marker kết thúc từ client {ClientIp}", clientIp);
                        break;
                    }

                    if (dataLength < 0 || dataLength > 5 * 1024 * 1024) // Giới hạn 5MB
                    {
                        _logger.LogWarning("Độ dài profile không hợp lệ từ client {ClientIp}: {Length}", clientIp, dataLength);
                        errorCount++;
                        await SendResponseAsync(stream, "ERROR:Invalid data length", stoppingToken);
                        continue;
                    }

                    byte[] dataBuffer = new byte[dataLength];
                    bytesRead = await stream.ReadAsync(dataBuffer, 0, dataLength, stoppingToken);
                    if (bytesRead < dataLength)
                    {
                        _logger.LogWarning("Dữ liệu profile không đầy đủ từ client {ClientIp}", clientIp);
                        errorCount++;
                        await SendResponseAsync(stream, "ERROR:Incomplete data", stoppingToken);
                        continue;
                    }

                    try
                    {
                        string json = Encoding.UTF8.GetString(dataBuffer, 0, bytesRead);
                        var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(json);

                        if (steamCmdProfile == null)
                        {
                            _logger.LogWarning("Dữ liệu profile không hợp lệ từ client {ClientIp}", clientIp);
                            errorCount++;
                            await SendResponseAsync(stream, "ERROR:Invalid profile data", stoppingToken);
                            continue;
                        }

                        // Chuyển đổi thành ClientProfile và thêm vào danh sách chờ
                        var clientProfile = new ClientProfile
                        {
                            Name = steamCmdProfile.Name,
                            AppID = steamCmdProfile.AppID,
                            InstallDirectory = steamCmdProfile.InstallDirectory,
                            Arguments = steamCmdProfile.Arguments ?? "",
                            ValidateFiles = steamCmdProfile.ValidateFiles,
                            AutoRun = steamCmdProfile.AutoRun,
                            AnonymousLogin = steamCmdProfile.AnonymousLogin,
                            Status = "Ready",
                            StartTime = DateTime.Now,
                            StopTime = DateTime.Now,
                            LastRun = DateTime.UtcNow
                        };

                        // Giữ nguyên thông tin đăng nhập không mã hóa lại
                        if (!steamCmdProfile.AnonymousLogin)
                        {
                            clientProfile.SteamUsername = steamCmdProfile.SteamUsername;
                            clientProfile.SteamPassword = steamCmdProfile.SteamPassword;
                        }

                        // Thêm vào danh sách chờ xác nhận
                        _syncService.AddPendingProfile(clientProfile);

                        processedCount++;
                        await SendResponseAsync(stream, $"SUCCESS:Added profile {clientProfile.Name}", stoppingToken);
                        _logger.LogInformation("Đã nhận profile {ProfileName} từ client {ClientIp}", clientProfile.Name, clientIp);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile từ client {ClientIp}", clientIp);
                        errorCount++;
                        await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
                    }
                }

                // Gửi thông báo hoàn thành
                await SendResponseAsync(stream, $"DONE:{processedCount}:{errorCount}", stoppingToken);
                _logger.LogInformation("Đã nhận {SuccessCount} profiles từ client {ClientIp}, {ErrorCount} lỗi",
                    processedCount, clientIp, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profiles từ client {ClientIp}", clientIp);
                try
                {
                    await SendResponseAsync(stream, $"ERROR:{ex.Message}", stoppingToken);
                }
                catch
                {
                    // Bỏ qua lỗi nếu không thể gửi phản hồi
                }
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, string response, CancellationToken stoppingToken)
        {
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            byte[] lengthBytes = BitConverter.GetBytes(responseBytes.Length);

            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, stoppingToken);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, stoppingToken);
            await stream.FlushAsync(stoppingToken);
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _isRunning = false;
            _tcpListener?.Stop();
            await base.StopAsync(stoppingToken);
        }
    }
}