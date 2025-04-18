using System;

namespace SteamCmdWeb.Models
{
    public class ClientRegistration
    {
        public string ClientId { get; set; }
        public string Description { get; set; }
        public string Address { get; set; }
        public int Port { get; set; } = 61188;
        public string AuthToken { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime RegisteredAt { get; set; } = DateTime.Now;
        public DateTime LastSuccessfulSync { get; set; }
        public DateTime LastSyncAttempt { get; set; }
        public string LastSyncResults { get; set; }
        public int ConnectionFailureCount { get; set; }
    }
}