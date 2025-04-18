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
        private readonly int _port = 61188;
        private TcpListener _listener;
        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(10, 10); // Giới hạn 10 kết nối đồng thời

        public TcpServerService(
            ILogger<TcpServerService> logger,
            ProfileService profileService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();

                _logger.LogInformation("TCP Server đã khởi động trên cổng {0}", _port);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        _ = ProcessClientAsync(client, stoppingToken);
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
                _logger.LogError(ex, "Lỗi khi khởi động TCP Server");
            }
            finally
            {
                _listener?.Stop();
                _logger.LogInformation("TCP Server đã dừng");
            }
        }

        private async Task ProcessClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            string clientIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
            _logger.LogInformation("Kết nối mới từ {0}", clientIp);

            await _connectionSemaphore.WaitAsync(stoppingToken);
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    client.ReceiveTimeout = 30000; // 30 giây timeout
                    client.SendTimeout = 30000;

                    // Đọc độ dài lệnh
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Kết nối từ {0} bị đóng đột ngột", clientIp);
                        return;
                    }

                    int commandLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (commandLength <= 0 || commandLength > 1024 * 1024) // Giới hạn 1MB
                    {
                        _logger.LogWarning("Độ dài lệnh không hợp lệ từ {0}: {1}", clientIp, commandLength);
                        return;
                    }

                    // Đọc lệnh
                    byte[] commandBuffer = new byte[commandLength];
                    bytesRead = await stream.ReadAsync(commandBuffer, 0, commandLength, stoppingToken);
                    if (bytesRead < commandLength)
                    {
                        _logger.LogWarning("Không đọc đủ dữ liệu lệnh từ {0}", clientIp);
                        return;
                    }

                    string command = Encoding.UTF8.GetString(commandBuffer, 0, bytesRead);
                    _logger.LogInformation("Nhận lệnh từ {0}: {1}", clientIp, command);

                    // Xử lý lệnh
                    if (command.StartsWith("AUTH:"))
                    {
                        // Phân tích lệnh
                        string[] parts = command.Split(new[] { ' ' }, 2);
                        if (parts.Length < 2)
                        {
                            await SendResponseAsync(stream, "INVALID_COMMAND");
                            return;
                        }

                        string authPart = parts[0];
                        string actualCommand = parts[1];

                        // Kiểm tra xác thực - đơn giản hóa
                        string[] authParts = authPart.Split(':');
                        if (authParts.Length < 2)
                        {
                            await SendResponseAsync(stream, "INVALID_AUTH");
                            return;
                        }

                        // Xử lý lệnh sau khi xác thực
                        if (actualCommand == "GET_PROFILES")
                        {
                            await HandleGetProfilesAsync(stream);
                        }
                        else if (actualCommand.StartsWith("GET_PROFILE_DETAILS"))
                        {
                            string[] cmdParts = actualCommand.Split(' ');
                            if (cmdParts.Length < 2)
                            {
                                await SendResponseAsync(stream, "MISSING_PROFILE_NAME");
                                return;
                            }

                            string profileName = cmdParts[1];
                            await HandleGetProfileDetailsAsync(stream, profileName);
                        }
                        else if (actualCommand == "SEND_PROFILES")
                        {
                            await SendResponseAsync(stream, "READY_TO_RECEIVE");
                            await HandleReceiveProfilesAsync(stream, clientIp);
                        }
                        else
                        {
                            await SendResponseAsync(stream, "UNKNOWN_COMMAND");
                        }
                    }
                    else
                    {
                        await SendResponseAsync(stream, "AUTH_REQUIRED");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Khi service đang dừng, bỏ qua lỗi
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý kết nối từ {0}", clientIp);
            }
            finally
            {
                _connectionSemaphore.Release();
                _logger.LogInformation("Đã đóng kết nối từ {0}", clientIp);
            }
        }

        private async Task SendResponseAsync(NetworkStream stream, string response)
        {
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            byte[] lengthBytes = BitConverter.GetBytes(responseBytes.Length);

            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
            await stream.FlushAsync();
        }

        private async Task HandleGetProfilesAsync(NetworkStream stream)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();

                if (profiles.Count == 0)
                {
                    await SendResponseAsync(stream, "NO_PROFILES");
                    return;
                }

                // Chỉ gửi danh sách tên profile
                List<string> profileNames = new List<string>();
                foreach (var profile in profiles)
                {
                    profileNames.Add(profile.Name);
                }

                string response = string.Join(",", profileNames);
                await SendResponseAsync(stream, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý yêu cầu GET_PROFILES");
                await SendResponseAsync(stream, "ERROR:INTERNAL_SERVER_ERROR");
            }
        }

        private async Task HandleGetProfileDetailsAsync(NetworkStream stream, string profileName)
        {
            try
            {
                var profile = await _profileService.GetProfileByNameAsync(profileName);

                if (profile == null)
                {
                    await SendResponseAsync(stream, "PROFILE_NOT_FOUND");
                    return;
                }

                // Sử dụng System.Text.Json để serialize profile
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                string json = JsonSerializer.Serialize(profile, options);
                await SendResponseAsync(stream, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý yêu cầu GET_PROFILE_DETAILS cho {0}", profileName);
                await SendResponseAsync(stream, "ERROR:INTERNAL_SERVER_ERROR");
            }
        }

        private async Task HandleReceiveProfilesAsync(NetworkStream stream, string clientIp)
        {
            try
            {
                int processedCount = 0;
                int errorCount = 0;

                // Đọc độ dài profile
                byte[] lengthBuffer = new byte[4];

                while (true)
                {
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Kết nối từ {0} bị đóng đột ngột khi nhận profiles", clientIp);
                        break;
                    }

                    int profileLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // Marker kết thúc
                    if (profileLength == 0)
                    {
                        _logger.LogInformation("Kết thúc nhận profiles từ {0}", clientIp);
                        break;
                    }

                    if (profileLength < 0 || profileLength > 5 * 1024 * 1024) // Giới hạn 5MB
                    {
                        _logger.LogWarning("Độ dài profile không hợp lệ từ {0}: {1}", clientIp, profileLength);
                        await SendResponseAsync(stream, "ERROR:INVALID_LENGTH");
                        errorCount++;
                        continue;
                    }

                    // Đọc dữ liệu profile
                    byte[] profileBuffer = new byte[profileLength];
                    bytesRead = await stream.ReadAsync(profileBuffer, 0, profileLength);

                    if (bytesRead < profileLength)
                    {
                        _logger.LogWarning("Không đọc đủ dữ liệu profile từ {0}", clientIp);
                        await SendResponseAsync(stream, "ERROR:INCOMPLETE_DATA");
                        errorCount++;
                        continue;
                    }

                    // Phân tích profile
                    string profileJson = Encoding.UTF8.GetString(profileBuffer, 0, bytesRead);

                    try
                    {
                        var profile = JsonSerializer.Deserialize<ClientProfile>(profileJson, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (profile == null)
                        {
                            await SendResponseAsync(stream, "ERROR:INVALID_PROFILE_DATA");
                            errorCount++;
                            continue;
                        }

                        // Kiểm tra tính hợp lệ của profile
                        if (string.IsNullOrEmpty(profile.Name) || string.IsNullOrEmpty(profile.AppID) || string.IsNullOrEmpty(profile.InstallDirectory))
                        {
                            await SendResponseAsync(stream, "ERROR:MISSING_REQUIRED_FIELDS");
                            errorCount++;
                            continue;
                        }

                        // Kiểm tra xem profile đã tồn tại chưa
                        var existingProfile = await _profileService.GetProfileByNameAsync(profile.Name);

                        if (existingProfile != null)
                        {
                            // Cập nhật profile hiện có
                            profile.Id = existingProfile.Id;
                            await _profileService.UpdateProfileAsync(profile);
                            await SendResponseAsync(stream, $"SUCCESS:UPDATED:{profile.Name}");
                        }
                        else
                        {
                            // Thêm profile mới
                            await _profileService.AddProfileAsync(profile);
                            await SendResponseAsync(stream, $"SUCCESS:ADDED:{profile.Name}");
                        }

                        processedCount++;
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Lỗi khi parse JSON profile từ {0}", clientIp);
                        await SendResponseAsync(stream, "ERROR:INVALID_JSON");
                        errorCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile từ {0}", clientIp);
                        await SendResponseAsync(stream, "ERROR:PROCESSING_FAILED");
                        errorCount++;
                    }
                }

                // Gửi phản hồi tổng kết
                await SendResponseAsync(stream, $"DONE:{processedCount}:{errorCount}");
                _logger.LogInformation("Đã nhận {0} profiles, {1} lỗi từ {2}", processedCount, errorCount, clientIp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profiles từ {0}", clientIp);
                await SendResponseAsync(stream, "ERROR:INTERNAL_SERVER_ERROR");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _listener?.Stop();
            _logger.LogInformation("TCP Server đã dừng");
            await base.StopAsync(cancellationToken);
        }
    }
}