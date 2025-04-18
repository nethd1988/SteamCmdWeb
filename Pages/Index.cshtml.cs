using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SteamCmdWeb.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ProfileService _profileService;
        private static readonly DateTime _serverStartTime = DateTime.Now;

        public string Uptime { get; private set; }
        public List<ClientProfile> Profiles { get; private set; } = new List<ClientProfile>();

        public IndexModel(ProfileService profileService)
        {
            _profileService = profileService;
        }

        public async Task OnGetAsync()
        {
            // Lấy danh sách profiles
            Profiles = await _profileService.GetAllProfilesAsync();

            // Tính thời gian uptime
            TimeSpan uptime = DateTime.Now - _serverStartTime;
            Uptime = FormatUptime(uptime);
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

            return $"{timeSpan.Minutes} phút, {timeSpan.Seconds} giây";
        }
    }
}