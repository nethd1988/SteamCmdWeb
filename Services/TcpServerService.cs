using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SteamCmdWeb.Services
{
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private TcpListener _server;
        private const int Port = 61188;

        public TcpServerService(ILogger<TcpServerService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Dịch vụ TCP Server đang khởi động trên cổng {Port}", Port);
                _server = new TcpListener(IPAddress.Any, Port);
                _server.Start();

                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Đang chờ kết nối TCP...");

                    try
                    {
                        using var client = await _server.AcceptTcpClientAsync(stoppingToken);
                        _logger.LogInformation("Đã chấp nhận kết nối từ {Endpoint}", client.Client.RemoteEndPoint);

                        // Xử lý kết nối trong một task khác
                        _ = HandleClientAsync(client, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Bình thường khi dừng dịch vụ
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý kết nối TCP");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi khởi động TCP Server");
            }
            finally
            {
                _server?.Stop();
                _logger.LogInformation("Dịch vụ TCP Server đã dừng");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            try
            {
                var buffer = new byte[4096];
                var stream = client.GetStream();
                var clientEndpoint = client.Client.RemoteEndPoint.ToString();

                // Đọc dữ liệu từ client
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                if (bytesRead > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    _logger.LogInformation("Đã nhận {BytesRead} bytes từ {ClientEndpoint}", bytesRead, clientEndpoint);

                    // Xử lý message và response
                    var response = $"SteamCmdWeb Server Time: {DateTime.Now}";
                    var responseBytes = Encoding.UTF8.GetBytes(response);

                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length, stoppingToken);
                    _logger.LogInformation("Đã gửi {BytesSent} bytes đến {ClientEndpoint}", responseBytes.Length, clientEndpoint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý client TCP");
            }
            finally
            {
                if (client.Connected)
                {
                    client.Close();
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Đang dừng dịch vụ TCP Server...");
            _server?.Stop();
            return base.StopAsync(cancellationToken);
        }
    }
}