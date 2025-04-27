using Microsoft.AspNetCore.Http;
using SteamCmdWeb.Services;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Middleware
{
    public class ClientTrackingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ClientTrackingMiddleware> _logger;

        public ClientTrackingMiddleware(RequestDelegate next, ILogger<ClientTrackingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ClientTrackingService clientTrackingService)
        {
            try
            {
                // Chỉ theo dõi các request từ client API (không phải web UI)
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    var clientId = context.Request.Headers["X-Client-Id"].ToString();
                    var remoteIp = context.Connection.RemoteIpAddress?.ToString();
                    var inverterIp = context.Request.Headers["X-Forwarded-For"].ToString();

                    if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(remoteIp))
                    {
                        clientTrackingService.TrackClient(clientId, remoteIp, inverterIp);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in client tracking middleware");
            }

            await _next(context);
        }
    }

    public static class ClientTrackingMiddlewareExtensions
    {
        public static IApplicationBuilder UseClientTracking(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ClientTrackingMiddleware>();
        }
    }
}