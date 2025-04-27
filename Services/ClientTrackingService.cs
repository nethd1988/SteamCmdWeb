using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Services
{
    public class ClientTrackingService
    {
        private readonly ILogger<ClientTrackingService> _logger;
        private readonly ConcurrentDictionary<string, ClientInfo> _connectedClients;

        public ClientTrackingService(ILogger<ClientTrackingService> logger)
        {
            _logger = logger;
            _connectedClients = new ConcurrentDictionary<string, ClientInfo>();
        }

        public void TrackClient(string clientId, string remoteIp, string inverterIp = null)
        {
            try
            {
                // Lấy công IP ngoài (inverter) nếu không được cung cấp
                if (string.IsNullOrEmpty(inverterIp))
                {
                    // Giả định inverter IP giống remote IP nếu không phải localhost
                    inverterIp = remoteIp.StartsWith("127.") || remoteIp.StartsWith("::1") ? null : remoteIp;
                }

                var clientInfo = new ClientInfo
                {
                    ClientId = clientId,
                    RemoteIp = remoteIp,
                    InverterIp = inverterIp,
                    ConnectedTime = DateTime.Now,
                    LastActiveTime = DateTime.Now,
                    Status = "Online",
                    ConnectionCount = 1
                };

                _connectedClients.AddOrUpdate(clientId, clientInfo, (key, existingInfo) =>
                {
                    existingInfo.LastActiveTime = DateTime.Now;
                    existingInfo.Status = "Online";
                    existingInfo.InverterIp = inverterIp ?? existingInfo.InverterIp;
                    existingInfo.ConnectionCount++;
                    return existingInfo;
                });

                _logger.LogInformation(
                    "Client tracked - ID: {ClientId}, IP: {RemoteIp}, Inverter: {InverterIp}, Count: {Count}",
                    clientId, remoteIp, inverterIp, clientInfo.ConnectionCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking client {ClientId}", clientId);
            }
        }

        public void UpdateClientStatus(string clientId, string status)
        {
            if (_connectedClients.TryGetValue(clientId, out var clientInfo))
            {
                clientInfo.Status = status;
                clientInfo.LastActiveTime = DateTime.Now;
                
                if (status == "Offline")
                {
                    clientInfo.DisconnectedTime = DateTime.Now;
                }
            }
        }

        public List<ClientInfo> GetAllClients()
        {
            return _connectedClients.Values.ToList();
        }

        public ClientInfo GetClientInfo(string clientId)
        {
            _connectedClients.TryGetValue(clientId, out var clientInfo);
            return clientInfo;
        }

        public void RemoveClient(string clientId)
        {
            _connectedClients.TryRemove(clientId, out _);
        }

        public void CheckAndUpdateInactiveClients(TimeSpan inactivityThreshold)
        {
            var now = DateTime.Now;
            foreach (var client in _connectedClients.Values)
            {
                if (client.Status == "Online" && (now - client.LastActiveTime) > inactivityThreshold)
                {
                    client.Status = "Offline";
                    client.DisconnectedTime = now;
                    _logger.LogInformation("Client {ClientId} marked as offline due to inactivity", client.ClientId);
                }
            }
        }

        public int GetOnlineClientCount()
        {
            return _connectedClients.Values.Count(c => c.Status == "Online");
        }

        public int GetTotalClientCount()
        {
            return _connectedClients.Count;
        }
    }

    public class ClientInfo
    {
        public string ClientId { get; set; }
        public string RemoteIp { get; set; }
        public string InverterIp { get; set; }
        public DateTime ConnectedTime { get; set; }
        public DateTime LastActiveTime { get; set; }
        public DateTime? DisconnectedTime { get; set; }
        public string Status { get; set; }
        public int ConnectionCount { get; set; }
    }
}