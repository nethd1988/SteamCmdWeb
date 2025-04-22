using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class SyncService
    {
        private readonly ILogger<SyncService> _logger;
        private readonly ProfileService _profileService;

        // Danh sách các profile đang chờ xác nhận
        private readonly ConcurrentBag<ClientProfile> _pendingProfiles = new ConcurrentBag<ClientProfile>();

        public SyncService(
            ILogger<SyncService> logger,
            ProfileService profileService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        }

        // Lấy danh sách profile đang chờ xác nhận
        public List<ClientProfile> GetPendingProfiles()
        {
            return _pendingProfiles.ToList();
        }

        // Thêm profile vào danh sách chờ xác nhận
        public void AddPendingProfile(ClientProfile profile)
        {
            _logger.LogInformation("Thêm profile vào danh sách chờ: Name={Name}, Username={Username}, Password={Password}, Anonymous={Anonymous}",
                profile.Name, profile.SteamUsername, profile.SteamPassword, profile.AnonymousLogin);

            _pendingProfiles.Add(profile);
        }

        // Xác nhận profile theo index
        public async Task<bool> ConfirmProfileAsync(int index)
        {
            var profiles = _pendingProfiles.ToList();
            if (index < 0 || index >= profiles.Count)
            {
                _logger.LogWarning("Index không hợp lệ: {Index}, khi xác nhận profile", index);
                return false;
            }

            var profile = profiles[index];

            try
            {
                // Kiểm tra trùng lặp
                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingProfile = existingProfiles.FirstOrDefault(p => p.Name == profile.Name);

                if (existingProfile != null)
                {
                    // Cập nhật profile hiện có
                    profile.Id = existingProfile.Id;
                    await _profileService.UpdateProfileAsync(profile);
                    _logger.LogInformation("Đã cập nhật profile {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                }
                else
                {
                    // Thêm profile mới
                    await _profileService.AddProfileAsync(profile);
                    _logger.LogInformation("Đã thêm profile mới: {ProfileName}", profile.Name);
                }

                // Xóa profile khỏi danh sách chờ
                RemovePendingProfile(index);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xác nhận profile {ProfileName}", profile.Name);
                return false;
            }
        }

        // Từ chối profile theo index
        public bool RejectProfile(int index)
        {
            var profiles = _pendingProfiles.ToList();
            if (index < 0 || index >= profiles.Count)
            {
                _logger.LogWarning("Index không hợp lệ: {Index}, khi từ chối profile", index);
                return false;
            }

            var profile = profiles[index];
            _logger.LogInformation("Đã từ chối profile: {ProfileName}", profile.Name);

            // Xóa profile khỏi danh sách chờ
            RemovePendingProfile(index);

            return true;
        }

        // Xác nhận tất cả các profile đang chờ
        public async Task<int> ConfirmAllPendingProfilesAsync()
        {
            var profiles = _pendingProfiles.ToList();
            if (profiles.Count == 0)
            {
                return 0;
            }

            int count = 0;
            var existingProfiles = await _profileService.GetAllProfilesAsync();

            foreach (var profile in profiles)
            {
                try
                {
                    // Kiểm tra trùng lặp
                    var existingProfile = existingProfiles.FirstOrDefault(p => p.Name == profile.Name);

                    if (existingProfile != null)
                    {
                        // Cập nhật profile hiện có
                        profile.Id = existingProfile.Id;
                        await _profileService.UpdateProfileAsync(profile);
                        _logger.LogInformation("Đã cập nhật profile {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);
                    }
                    else
                    {
                        // Thêm profile mới
                        await _profileService.AddProfileAsync(profile);
                        _logger.LogInformation("Đã thêm profile mới: {ProfileName}", profile.Name);
                    }

                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi xác nhận profile {ProfileName}", profile.Name);
                }
            }

            // Xóa tất cả các profile đã được xác nhận
            _pendingProfiles.Clear();

            return count;
        }

        // Từ chối tất cả các profile đang chờ
        public int RejectAllPendingProfiles()
        {
            var count = _pendingProfiles.Count;
            _pendingProfiles.Clear();
            _logger.LogInformation("Đã từ chối tất cả {Count} profile đang chờ", count);
            return count;
        }

        // Xóa profile khỏi danh sách chờ theo index
        private void RemovePendingProfile(int index)
        {
            // Xóa profile khỏi _pendingProfiles theo index
            var profiles = _pendingProfiles.ToList();
            _pendingProfiles.Clear();

            for (int i = 0; i < profiles.Count; i++)
            {
                if (i != index)
                {
                    _pendingProfiles.Add(profiles[i]);
                }
            }
        }
    }
}