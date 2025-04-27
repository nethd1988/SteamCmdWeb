using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SteamCmdWeb.Services
{
    public class ClientInactivityService : BackgroundService
    {
        private readonly ILogger<ClientInactivityService> _logger;
        private readonly ClientTrackingService _clientTrackingService;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _inactivityThreshold = TimeSpan.FromMinutes(5);

        public ClientInactivityService(
            ILogger<ClientInactivityService> logger,
            ClientTrackingService clientTrackingService)
        {
            _logger = logger;
            _clientTrackingService = clientTrackingService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Client Inactivity Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _clientTrackingService.CheckAndUpdateInactiveClients(_inactivityThreshold);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking inactive clients");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("Client Inactivity Service stopped");
        }
    }
}