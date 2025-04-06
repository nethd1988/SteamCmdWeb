using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamCmdWeb.Services;
using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace SteamCmdWeb.Pages
{
    public class IndexModel : PageModel
    {
        private readonly AppProfileManager _profileManager;
        private static readonly DateTime _serverStartTime = DateTime.Now;

        public bool IsServerRunning { get; private set; }
        public string Uptime { get; private set; }
        public string StartTime { get; private set; }

        public IndexModel(AppProfileManager profileManager)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        }

        public void OnGet()
        {
            IsServerRunning = IsPortListening(61188);
            TimeSpan uptime = DateTime.Now - _serverStartTime;
            Uptime = FormatUptime(uptime);
            StartTime = _serverStartTime.ToString("dd/MM/yyyy HH:mm:ss");
        }

        private bool IsPortListening(int port)
        {
            try
            {
                IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpListeners = ipProperties.GetActiveTcpListeners();
                return tcpListeners.Any(endpoint => endpoint.Port == port);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string FormatUptime(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
            {
                return $"{(int)timeSpan.TotalDays} ngày, {timeSpan.Hours} giờ, {timeSpan.Minutes} phút";
            }

            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours} giờ, {timeSpan.Minutes} phút";
            }

            if (timeSpan.TotalMinutes >= 1)
            {
                return $"{(int)timeSpan.TotalMinutes} phút, {timeSpan.Seconds} giây";
            }

            return $"{timeSpan.Seconds} giây";
        }
    }
}