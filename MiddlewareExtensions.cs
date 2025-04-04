using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Builder;
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
                if (context.Request.Path.StartsWithSegments("/api/silentsync") &&
                    context.Request.Method == "POST")
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation("Processing silent sync request from {IpAddress}",
                        context.Connection.RemoteIpAddress);

                    string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    DateTime timestamp = DateTime.Now;
                    string logEntry = $"{timestamp:yyyy-MM-dd HH:mm:ss} - {clientIp} - Silent Sync Request{Environment.NewLine}";

                    try
                    {
                        string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");
                        if (!Directory.Exists(logDir))
                        {
                            Directory.CreateDirectory(logDir);
                        }

                        string logFilePath = Path.Combine(logDir, $"silentsync_{DateTime.Now:yyyyMMdd}.log");
                        File.AppendAllText(logFilePath, logEntry);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error writing to silent sync log file");
                    }
                }

                await next();
            });
        }

        /// <summary>
        /// Kiểm tra xem request có phải là local không
        /// </summary>
        private static bool IsLocalRequest(Microsoft.AspNetCore.Http.HttpContext context)
        {
            var connection = context.Connection;
            if (connection.RemoteIpAddress == null) return true;
            if (connection.LocalIpAddress == null) return IPAddress.IsLoopback(connection.RemoteIpAddress);

            return connection.RemoteIpAddress.Equals(connection.LocalIpAddress) ||
                   IPAddress.IsLoopback(connection.RemoteIpAddress);
        }

        /// <summary>
        /// Đăng ký các dịch vụ đồng bộ hóa
        /// </summary>
        public static IServiceCollection AddSyncServices(this IServiceCollection services)
        {
            services.AddSingleton<AppProfileManager>();
            services.AddHostedService<TcpServerService>();

            return services;
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

                if (!IsLocalRequest(context))
                {
                    logger.LogInformation("Remote request: {Method} {Path} from {IpAddress}",
                        method, path, clientIp);

                    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {clientIp} - {method} {path}{Environment.NewLine}";

                    try
                    {
                        string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");
                        if (!Directory.Exists(logDir))
                        {
                            Directory.CreateDirectory(logDir);
                        }

                        string logFilePath = Path.Combine(logDir, $"remote_requests_{DateTime.Now:yyyyMMdd}.log");
                        File.AppendAllText(logFilePath, logEntry);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error writing to remote request log file");
                    }
                }

                await next();
            });
        }
    }
}