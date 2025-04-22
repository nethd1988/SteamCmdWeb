using System;

namespace SteamCmdWeb.Models
{
    public class SyncResult
    {
        public string ClientId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public int TotalProfiles { get; set; }
        public int NewProfilesAdded { get; set; }
        public int FilteredProfiles { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}