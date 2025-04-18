using System;
using System.Collections.Generic;
using System.Linq;
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
        private TcpListener _tcpListener;
        private readonly int _port = 61188;
        private CancellationTokenSource _stoppingCts = new CancellationTokenSource();

        public TcpServerService(
            ILogger<TcpServerService> logger,
            ProfileService profileService,
            DecryptionService decryptionService)
        {
            _logger = logger;
            _profileService = profileService;
            _decryptionService = decryptionService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("TCP Server đang khởi động trên cổng {Port}...", _port);
                _tcpListener = new TcpListener(IPAddress.Any, _port);
                _tcpListener.Start();
                _logger.LogInformation("TCP Server đã khởi động thành công trên cổng {Port}", _port);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var tcpClient = await _tcpListener.AcceptTcpClientAsync(stoppingToken);
                        _ = ProcessClientAsync(tcpClient, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancelled by stopping token, exit gracefully
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
                _logger.LogError(ex, "Lỗi khi khởi động TCP Server");
            }
            finally
            {
                _tcpListener?.Stop();
                _logger.LogInformation("TCP Server đã dừng");
            }
        }

        private async Task ProcessClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            string clientAddress = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            _logger.LogInformation("Đã nhận kết nối từ {ClientAddress}", clientAddress);

            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    // Đọc chiều dài command
                    byte[] lenBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lenBuffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Kết nối từ {ClientAddress} bị đóng đột ngột", clientAddress);
                        return;
                    }

                    int commandLength = BitConverter.ToInt32(lenBuffer, 0);
                    if (commandLength <= 0 || commandLength > 8192) // Max 8KB command
                    {
                        _logger.LogWarning("Độ dài command không hợp lệ: {Length} từ {ClientAddress}", commandLength, clientAddress);
                        return;
                    }

                    // Đọc command
                    byte[] commandBuffer = new byte[commandLength];
                    bytesRead = await stream.ReadAsync(commandBuffer, 0, commandLength, stoppingToken);
                    if (bytesRead < commandLength)
                    {
                        _logger.LogWarning("Command không đầy đủ từ {ClientAddress}", clientAddress);
                        return;
                    }

                    string command = Encoding.UTF8.GetString(commandBuffer, 0, bytesRead);
                    _logger.LogInformation("Nhận command từ {ClientAddress}: {Command}", clientAddress, command);

                    // Xử lý command
                    string response = await ProcessCommandAsync(command, clientAddress);
                    byte[] responseData = Encoding.UTF8.GetBytes(response);
                    byte[] responseLength = BitConverter.GetBytes(responseData.Length);

                    // Gửi response
                    await stream.WriteAsync(responseLength, 0, responseLength.Length, stoppingToken);
                    await stream.WriteAsync(responseData, 0, responseData.Length, stoppingToken);
                    await stream.FlushAsync(stoppingToken);

                    _logger.LogInformation("Đã gửi response thành công tới {ClientAddress}, độ dài: {Length}", clientAddress, responseData.Length);
                }
                catch (OperationCanceledException)
                {
                    // Cancelled by stopping token
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi xử lý client {ClientAddress}", clientAddress);
                }
            }
        }

        private async Task<string> ProcessCommandAsync(string command, string clientAddress)
        {
            try
            {
                // Xác thực (đơn giản)
                const string authToken = "simple_auth_token";
                if (!command.StartsWith("AUTH:" + authToken))
                {
                    _logger.LogWarning("Xác thực thất bại từ {ClientAddress}: {Command}", clientAddress, command);
                    return "AUTH_FAILED";
                }

                // Xử lý loại command
                string[] parts = command.Substring(("AUTH:" + authToken).Length).Trim().Split(' ', 2);
                string cmd = parts[0].Trim();

                switch (cmd)
                {
                    case "GET_PROFILES":
                        return await GetProfilesAsync();
                    case "GET_PROFILE_DETAILS":
                        if (parts.Length < 2)
                        {
                            return "MISSING_PROFILE_NAME";
                        }
                        return await GetProfileDetailsAsync(parts[1].Trim());
                    case "SEND_PROFILES":
                        return "READY_TO_RECEIVE";
                    default:
                        _logger.LogWarning("Command không được hỗ trợ từ {ClientAddress}: {Command}", clientAddress, cmd);
                        return "COMMAND_NOT_SUPPORTED";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý command từ {ClientAddress}", clientAddress);
                return "ERROR:" + ex.Message;
            }
        }

        private async Task<string> GetProfilesAsync()
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                if (profiles == null || profiles.Count == 0)
                {
                    return "NO_PROFILES";
                }

                return string.Join(",", profiles.Select(p => p.Name));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách profiles");
                return "ERROR:" + ex.Message;
            }
        }

        private async Task<string> GetProfileDetailsAsync(string profileName)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

                if (profile == null)
                {
                    return "PROFILE_NOT_FOUND";
                }

                // Chuyển đổi từ ClientProfile sang SteamCmdProfile
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

                return JsonSerializer.Serialize(steamCmdProfile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy chi tiết profile {ProfileName}", profileName);
                return "ERROR:" + ex.Message;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("TCP Server đang dừng...");
            _stoppingCts.Cancel();
            _tcpListener?.Stop();
            await base.StopAsync(cancellationToken);
        }
    }
}