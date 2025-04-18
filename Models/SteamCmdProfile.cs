﻿namespace SteamCmdWebAPI.Models
{
    public class SteamCmdProfile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string AppID { get; set; }
        public string InstallDirectory { get; set; }
        public string SteamUsername { get; set; }
        public string SteamPassword { get; set; }
        public string Arguments { get; set; }
        public bool ValidateFiles { get; set; }
        public bool AutoRun { get; set; }
        public bool AnonymousLogin { get; set; }
        public string Status { get; set; }
        public System.DateTime StartTime { get; set; }
        public System.DateTime StopTime { get; set; }
        public int Pid { get; set; }
        public System.DateTime? LastRun { get; set; }

        public SteamCmdProfile()
        {
            Name = "";
            AppID = "";
            InstallDirectory = "";
            SteamUsername = "";
            SteamPassword = "";
            Arguments = "";
            Status = "Stopped";
            Pid = 0;
            ValidateFiles = false;
            AutoRun = false;
            AnonymousLogin = false;
        }
    }
}