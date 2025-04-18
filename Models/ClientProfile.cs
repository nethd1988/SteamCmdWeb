using System;
using System.Text.Json.Serialization;

namespace SteamCmdWeb.Models
{
    public class ClientProfile
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AppID { get; set; } = string.Empty;
        public string InstallDirectory { get; set; } = string.Empty;
        public string SteamUsername { get; set; } = string.Empty;
        public string SteamPassword { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public bool ValidateFiles { get; set; }
        public bool AutoRun { get; set; }
        public bool AnonymousLogin { get; set; }
        public string Status { get; set; } = "Ready";
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime StopTime { get; set; } = DateTime.Now;
        public int Pid { get; set; }
        public DateTime LastRun { get; set; } = DateTime.UtcNow;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? InstallSize { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SessionCount { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Notes { get; set; }
    }
}