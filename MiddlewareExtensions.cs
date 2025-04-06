using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;

namespace SteamCmdWeb
{
    public static class MiddlewareExtensions
    {
        /// <summary>
        /// Cấu hình middleware để xử lý các yêu cầu đồng bộ âm thầm
        /// </summary>
        public static IApplicationBuilder UseSilentSyncMiddleware(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                bool isSilentSyncRequest = context.Request.Path.StartsWithSegments("/api/silentsync") &&
                                           context.Request.Method == "POST";

                if (isSilentSyncRequest)
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    DateTime timestamp = DateTime.Now;

                    logger.LogInformation("Nhận yêu cầu silent sync từ {IpAddress}", clientIp);

                    // Ghi log đồng bộ
                    try
                    {
                        string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");
                        if (!Directory.Exists(logDir))
                        {
                            Directory.CreateDirectory(logDir);
                        }

                        string logFilePath = Path.Combine(logDir, $"silentsync_{DateTime.Now:yyyyMMdd}.log");
                        string logEntry = $"{timestamp:yyyy-MM-dd HH:mm:ss} - {clientIp} - Silent Sync Request{Environment.NewLine}";

                        await File.AppendAllTextAsync(logFilePath, logEntry);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Lỗi khi ghi log silentsync");
                    }

                    // Cho phép đọc body request nhiều lần
                    context.Request.EnableBuffering();

                    // Lưu vị trí ban đầu của stream
                    var position = context.Request.Body.Position;

                    // Sao lưu body request thô
                    if (context.Request.ContentLength > 0 && context.Request.ContentLength < 50 * 1024 * 1024) // Giới hạn 50MB
                    {
                        try
                        {
                            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "SilentSync", "Raw");
                            if (!Directory.Exists(backupFolder))
                            {
                                Directory.CreateDirectory(backupFolder);
                            }

                            string backupFilePath = Path.Combine(backupFolder, $"request_{clientIp.Replace(":", "_")}_{timestamp:yyyyMMddHHmmss}.raw");

                            using (var outputStream = new FileStream(backupFilePath, FileMode.Create))
                            {
                                await context.Request.Body.CopyToAsync(outputStream);
                            }

                            // Đặt lại vị trí stream để middleware tiếp theo có thể đọc
                            context.Request.Body.Position = position;
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Không thể sao lưu body request thô");
                        }
                    }
                }

                await next();

                // Ghi log response nếu là silent sync request
                if (isSilentSyncRequest)
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    logger.LogInformation("Đã xử lý yêu cầu silent sync từ {IpAddress}, Status: {StatusCode}",
                        clientIp, context.Response.StatusCode);
                }
            });
        }

        /// <summary>
        /// Kiểm tra xem request có phải là local không
        /// </summary>
        private static bool IsLocalRequest(HttpContext context)
        {
            var connection = context.Connection;
            if (connection.RemoteIpAddress == null) return true;
            if (connection.LocalIpAddress == null) return IPAddress.IsLoopback(connection.RemoteIpAddress);

            return connection.RemoteIpAddress.Equals(connection.LocalIpAddress) ||
                   IPAddress.IsLoopback(connection.RemoteIpAddress);
        }

        /// <summary>
        /// Cấu hình middleware để xử lý các yêu cầu từ xa
        /// </summary>
        public static IApplicationBuilder UseRemoteRequestLogging(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                string path = context.Request.Path;
                string method = context.Request.Method;

                // Chỉ log các yêu cầu từ xa, bỏ qua các yêu cầu local 
                if (!IsLocalRequest(context) && !path.StartsWith("/api/silentsync")) // Tránh log trùng lặp với SilentSync
                {
                    // Log các yêu cầu có phương thức thay đổi dữ liệu
                    if (method != "GET" && method != "HEAD" && method != "OPTIONS")
                    {
                        logger.LogInformation("Remote {Method} request: {Path} from {IpAddress}",
                            method, path, clientIp);
                    }
                    else
                    {
                        // Chỉ log ở mức Debug cho GET để tránh spam log
                        logger.LogDebug("Remote {Method} request: {Path} from {IpAddress}",
                            method, path, clientIp);
                    }

                    try
                    {
                        string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");
                        if (!Directory.Exists(logDir))
                        {
                            Directory.CreateDirectory(logDir);
                        }

                        string logFilePath = Path.Combine(logDir, $"remote_requests_{DateTime.Now:yyyyMMdd}.log");
                        string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {clientIp} - {method} {path}{Environment.NewLine}";

                        await File.AppendAllTextAsync(logFilePath, logEntry);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Lỗi khi ghi log yêu cầu từ xa");
                    }
                }

                // Đo thời gian xử lý request
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    // Log lỗi không bắt được ở middleware khác
                    logger.LogError(ex, "Lỗi không xử lý khi xử lý yêu cầu {Method} {Path} từ {IpAddress}",
                        method, path, clientIp);
                    throw;
                }
                finally
                {
                    stopwatch.Stop();

                    // Log các yêu cầu mất nhiều thời gian (trên 1 giây)
                    if (stopwatch.ElapsedMilliseconds > 1000 && !IsLocalRequest(context))
                    {
                        logger.LogWarning("Request {Method} {Path} từ {IpAddress} mất {ElapsedMs}ms để xử lý",
                            method, path, clientIp, stopwatch.ElapsedMilliseconds);
                    }
                }
            });
        }
    }
}