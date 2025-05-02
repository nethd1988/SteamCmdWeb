using System;

namespace SteamCmdWeb.Models
{
    public class SteamAccount
    {
        public int Id { get; set; }
        public string ProfileName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string AppIds { get; set; } // VD: "570,730,440"
        public string GameNames { get; set; } // VD: "Dota 2,CS:GO,Left 4 Dead"
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
} 