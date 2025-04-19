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

namespace SteamCmdWeb.Services
{
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;
        private readonly SynchronizationContext _syncContext;
        private readonly int _port = 61188;
        private TcpListener _listener;
        private bool _isRunning = false;
        private const string AUTH_TOKEN = "simple_auth_token";

        public TcpServerService(
            ILogger<TcpServerService> logger,
            ProfileService profileService,
            DecryptionService decryptionService)
        {
            _logger = logger;
            _profileService = profileService;
            _decryptionService = decryptionService;
            _syncContext = SynchronizationContext.Current;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                _isRunning = true;

                _logger.LogInformation("TCP Server khởi động trên cổng {Port}", _port);

                while (!stoppingToken.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        // Chấp nhận client mới một cách không đồng bộ
                        var client = await _listener.AcceptTcpClientAsync();
                        string clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

                        _logger.LogInformation("Client mới kết nối: {ClientEndpoint}", clientEndpoint);

                        // Xử lý client trong một task riêng biệt
                        _ = Task.Run(async () =>
                        {
                            using (client)
                            {
                                await HandleClientAsync(client, stoppingToken);
                            }
                        }, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancelled
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi chấp nhận client mới");

                        // Tạm dừng một chút để tránh vòng lặp liên tục khi gặp lỗi
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi khởi động TCP Server");
            }
            finally
            {
                if (_listener != null)
                {
                    _listener.Stop();
                    _logger.LogInformation("TCP Server đã dừng");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            string clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

            try
            {
                using var stream = client.GetStream();

                // Đọc dữ liệu từ client
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, cancellationToken);

                if (bytesRead < 4)
                {
                    _logger.LogWarning("Kết nối từ {ClientEndpoint} đã đóng trước khi đọc đủ dữ liệu", clientEndpoint);
                    return;
                }

                int commandLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (commandLength <= 0 || commandLength > 1024 * 1024) // Giới hạn 1MB để tránh tấn công
                {
                    _logger.LogWarning("Độ dài lệnh không hợp lệ từ {ClientEndpoint}: {Length}", clientEndpoint, commandLength);
                    return;
                }

                byte[] commandBuffer = new byte[commandLength];
                bytesRead = await stream.ReadAsync(commandBuffer, 0, commandLength, cancellationToken);

                if (bytesRead < commandLength)
                {
                    _logger.LogWarning("Không nhận đủ dữ liệu từ {ClientEndpoint}", clientEndpoint);
                    return;
                }

                string command = Encoding.UTF8.GetString(commandBuffer, 0, bytesRead);
                _logger.LogDebug("Nhận lệnh từ {ClientEndpoint}: {Command}", clientEndpoint, command);

                // Xác thực và xử lý lệnh
                if (!command.StartsWith("AUTH:" + AUTH_TOKEN))
                {
                    _logger.LogWarning("Xác thực thất bại từ {ClientEndpoint}", clientEndpoint);
                    await SendResponseAsync(stream, "AUTH_FAILED");
                    return;
                }

                // Xử lý các lệnh được hỗ trợ
                if (command.Contains("GET_PROFILES"))
                {
                    await HandleGetProfilesAsync(stream, command.Contains("GET_PROFILES_FULL"));
                }
                else if (command.Contains("SEND_PROFILES"))
                {
                    await HandleSendProfilesAsync(stream, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Lệnh không được hỗ trợ từ {ClientEndpoint}: {Command}", clientEndpoint, command);
                    await SendResponseAsync(stream, "UNSUPPORTED_COMMAND");
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý client {ClientEndpoint}", clientEndpoint);
            }
        }

        private async Task HandleGetProfilesAsync(NetworkStream stream, bool fullDetails)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync();

                if (profiles.Count == 0)
                {
                    await SendResponseAsync(stream, "NO_PROFILES");
                    return;
                }

                if (fullDetails)
                {
                    // Chuyển đổi danh sách ClientProfile sang SteamCmdProfile để tương thích với client
                    var steamProfiles = profiles.Select(p => new SteamCmdWebAPI.Models.SteamCmdProfile
                    {
                        Id = p.Id,
                        Name = p.Name,
                        AppID = p.AppID,
                        InstallDirectory = p.InstallDirectory,
                        SteamUsername = p.SteamUsername,
                        SteamPassword = p.SteamPassword,
                        Arguments = p.Arguments,
                        ValidateFiles = p.ValidateFiles,
                        AutoRun = p.AutoRun,
                        AnonymousLogin = p.AnonymousLogin,
                        Status = p.Status,
                        StartTime = p.StartTime,
                        StopTime = p.StopTime,
                        Pid = p.Pid,
                        LastRun = p.LastRun
                    }).ToList();

                    string json = JsonSerializer.Serialize(steamProfiles);
                    await SendResponseAsync(stream, json);
                }
                else
                {
                    // Chỉ gửi danh sách tên
                    string profileNames = string.Join(",", profiles.Select(p => p.Name));
                    await SendResponseAsync(stream, profileNames);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý lệnh GET_PROFILES");
                await SendResponseAsync(stream, "ERROR:" + ex.Message);
            }
        }

        private async Task HandleSendProfilesAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            try
            {
                // Gửi tín hiệu sẵn sàng nhận profile
                await SendResponseAsync(stream, "READY_TO_RECEIVE");

                int newProfiles = 0;
                int processedProfiles = 0;
                int errorCount = 0;

                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Đọc độ dài profile
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, cancellationToken);

                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Kết nối đã đóng trong quá trình nhận profile");
                        break;
                    }

                    int profileLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // Marker kết thúc: độ dài = 0
                    if (profileLength == 0)
                    {
                        _logger.LogInformation("Nhận marker kết thúc");
                        break;
                    }

                    if (profileLength < 0 || profileLength > 10 * 1024 * 1024) // Giới hạn 10MB
                    {
                        _logger.LogWarning("Độ dài profile không hợp lệ: {Length}", profileLength);
                        await SendResponseAsync(stream, "ERROR:Invalid profile length");
                        errorCount++;
                        continue;
                    }

                    // Đọc nội dung profile
                    byte[] profileBuffer = new byte[profileLength];
                    bytesRead = await stream.ReadAsync(profileBuffer, 0, profileLength, cancellationToken);

                    if (bytesRead < profileLength)
                    {
                        _logger.LogWarning("Không nhận đủ dữ liệu profile");
                        await SendResponseAsync(stream, "ERROR:Incomplete profile data");
                        errorCount++;
                        continue;
                    }

                    string profileJson = Encoding.UTF8.GetString(profileBuffer, 0, bytesRead);

                    try
                    {
                        // Chuyển đổi từ JSON sang SteamCmdProfile
                        var steamProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(profileJson);

                        if (steamProfile == null)
                        {
                            _logger.LogWarning("Không thể chuyển đổi JSON thành profile");
                            await SendResponseAsync(stream, "ERROR:Invalid profile format");
                            errorCount++;
                            continue;
                        }

                        processedProfiles++;

                        // Chỉ lấy những profile có AppID chưa tồn tại
                        if (!existingAppIds.Contains(steamProfile.AppID))
                        {
                            // Chuyển đổi từ SteamCmdProfile sang ClientProfile
                            var clientProfile = new ClientProfile
                            {
                                Name = steamProfile.Name,
                                AppID = steamProfile.AppID,
                                InstallDirectory = steamProfile.InstallDirectory,
                                SteamUsername = steamProfile.SteamUsername,
                                SteamPassword = steamProfile.SteamPassword,
                                Arguments = steamProfile.Arguments,
                                ValidateFiles = steamProfile.ValidateFiles,
                                AutoRun = steamProfile.AutoRun,
                                AnonymousLogin = steamProfile.AnonymousLogin,
                                Status = "Ready",
                                StartTime = DateTime.Now,
                                StopTime = DateTime.Now,
                                LastRun = DateTime.UtcNow
                            };

                            await _profileService.AddProfileAsync(clientProfile);
                            existingAppIds.Add(steamProfile.AppID); // Cập nhật để không thêm trùng
                            newProfiles++;

                            await SendResponseAsync(stream, $"SUCCESS:{steamProfile.Name}");
                        }
                        else
                        {
                            await SendResponseAsync(stream, $"SKIP:{steamProfile.Name}:Already exists");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile từ client");
                        await SendResponseAsync(stream, $"ERROR:{ex.Message}");
                        errorCount++;
                    }
                }

                await SendResponseAsync(stream, $"DONE:{processedProfiles}:{errorCount}");
                _logger.LogInformation("Hoàn thành nhận profiles từ client. Đã thêm: {NewProfiles}, Lỗi: {ErrorCount}",
                    newProfiles, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý lệnh SEND_PROFILES");
                await SendResponseAsync(stream, "ERROR:" + ex.Message);
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

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _isRunning = false;
            _listener?.Stop();

            _logger.LogInformation("TCP Server đã nhận lệnh dừng");

            await base.StopAsync(cancellationToken);
        }
    }
}