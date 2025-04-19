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

namespace SteamCmdWeb.Services
{
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private readonly ProfileService _profileService;
        private readonly SyncService _syncService;
        private readonly int _port = 61188;
        private TcpListener _listener;

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
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                _logger.LogInformation("TCP Server đã khởi động và lắng nghe trên cổng {Port}", _port);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        _ = HandleClientAsync(client, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Lỗi khi chấp nhận kết nối client");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi khởi động TCP Server");
            }
            finally
            {
                _listener?.Stop();
                _logger.LogInformation("TCP Server đã dừng");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            try
            {
                using (client)
                {
                    var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    _logger.LogInformation("Client kết nối từ {RemoteEndPoint}", remoteEndPoint);

                    var stream = client.GetStream();
                    stream.ReadTimeout = 30000; // 30 giây timeout
                    stream.WriteTimeout = 30000;

                    // Đọc độ dài message
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Không đọc đủ thông tin độ dài từ client {RemoteEndPoint}", remoteEndPoint);
                        return;
                    }

                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (messageLength <= 0 || messageLength > 1024 * 1024) // Giới hạn 1MB
                    {
                        _logger.LogWarning("Độ dài message không hợp lệ từ client {RemoteEndPoint}: {Length}", remoteEndPoint, messageLength);
                        return;
                    }

                    // Đọc message
                    byte[] messageBuffer = new byte[messageLength];
                    bytesRead = await stream.ReadAsync(messageBuffer, 0, messageLength, stoppingToken);
                    if (bytesRead < messageLength)
                    {
                        _logger.LogWarning("Không đọc đủ message từ client {RemoteEndPoint}", remoteEndPoint);
                        return;
                    }

                    string message = Encoding.UTF8.GetString(messageBuffer, 0, bytesRead);
                    _logger.LogInformation("Nhận message từ client {RemoteEndPoint}: {MessagePreview}", remoteEndPoint, message.Length > 100 ? message.Substring(0, 100) + "..." : message);

                    // Xử lý message
                    string response = await ProcessMessageAsync(message, remoteEndPoint);

                    // Gửi response
                    byte[] responseData = Encoding.UTF8.GetBytes(response);
                    byte[] responseLengthData = BitConverter.GetBytes(responseData.Length);

                    await stream.WriteAsync(responseLengthData, 0, responseLengthData.Length, stoppingToken);
                    await stream.WriteAsync(responseData, 0, responseData.Length, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý client");
            }
        }

        private async Task<string> ProcessMessageAsync(string message, string clientEndPoint)
        {
            try
            {
                // Kiểm tra nếu message bắt đầu bằng "AUTH:"
                if (message.StartsWith("AUTH:"))
                {
                    string[] parts = message.Split(' ', 2);
                    if (parts.Length < 2)
                    {
                        return "INVALID_COMMAND";
                    }

                    string authPart = parts[0];
                    string commandPart = parts[1];

                    // Kiểm tra token
                    string[] authTokenParts = authPart.Split(':', 2);
                    if (authTokenParts.Length != 2 || authTokenParts[1] != "simple_auth_token")
                    {
                        return "INVALID_AUTH_TOKEN";
                    }

                    // Xử lý lệnh
                    if (commandPart == "GET_PROFILES")
                    {
                        return await GetProfilesListAsync();
                    }
                    else if (commandPart.StartsWith("GET_PROFILE_DETAILS "))
                    {
                        string profileName = commandPart.Substring("GET_PROFILE_DETAILS ".Length).Trim();
                        return await GetProfileDetailsByNameAsync(profileName);
                    }
                    else if (commandPart == "SEND_PROFILES")
                    {
                        return "READY_TO_RECEIVE";
                    }
                    else
                    {
                        return "UNKNOWN_COMMAND";
                    }
                }
                else
                {
                    return "MISSING_AUTH";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý message từ client {ClientEndPoint}", clientEndPoint);
                return "ERROR: " + ex.Message;
            }
        }

        private async Task<string> GetProfilesListAsync()
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();
                if (profiles.Count == 0)
                {
                    return "NO_PROFILES";
                }

                var profileNames = profiles.Select(p => p.Name).ToList();
                return string.Join(",", profileNames);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách profile");
                return "ERROR: " + ex.Message;
            }
        }

        private async Task<string> GetProfileDetailsByNameAsync(string profileName)
        {
            try
            {
                if (string.IsNullOrEmpty(profileName))
                {
                    return "PROFILE_NAME_REQUIRED";
                }

                var profiles = await _profileService.GetAllProfilesAsync();
                var profile = profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

                if (profile == null)
                {
                    return "PROFILE_NOT_FOUND";
                }

                return JsonSerializer.Serialize(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy chi tiết profile {ProfileName}", profileName);
                return "ERROR: " + ex.Message;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _listener?.Stop();
            await base.StopAsync(cancellationToken);
        }
    }
}