using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace SteamCmdWeb.Models
{
    [DebuggerDisplay("Name={Name}, Username={SteamUsername}, Password={SteamPassword}")]
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
        public string Status { get; set; } = "Ready";
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime StopTime { get; set; } = DateTime.Now;
        public int Pid { get; set; }
        public DateTime LastRun { get; set; } = DateTime.UtcNow;
    }
}