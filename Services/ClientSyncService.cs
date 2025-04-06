using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class SilentSyncService
    {
        private readonly AppProfileManager _profileManager;
        private readonly ILogger<SilentSyncService> _logger;
        private readonly string _logPath;

        public SilentSyncService(AppProfileManager profileManager, ILogger<SilentSyncService> logger)
        {
            _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Logs");

            if (!Directory.Exists(_logPath))
            {
                Directory.CreateDirectory(_logPath);
            }
        }

        public async Task<(bool Success, string Message)> ReceiveProfileAsync(ClientProfile profile, string clientIp)
        {
            try
            {
                if (profile == null)
                {
                    return (false, "Profile is null");
                }

                var existingProfile = _profileManager.GetProfileById(profile.Id);
                if (existingProfile == null)
                {
                    _profileManager.AddProfile(profile);
                    await LogSyncAction($"Added profile {profile.Name} (ID: {profile.Id})", clientIp);
                    return (true, $"Profile {profile.Name} added successfully");
                }
                else
                {
                    _profileManager.UpdateProfile(profile);
                    await LogSyncAction($"Updated profile {profile.Name} (ID: {profile.Id})", clientIp);
                    return (true, $"Profile {profile.Name} updated successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving profile from {ClientIp}", clientIp);
                return (false, $"Error: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message, int AddedCount, int UpdatedCount, int ErrorCount, List<int> ProcessedIds)> ReceiveProfilesAsync(List<ClientProfile> profiles, string clientIp)
        {
            try
            {
                int addedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;
                var processedIds = new List<int>();

                foreach (var profile in profiles)
                {
                    var existingProfile = _profileManager.GetProfileById(profile.Id);
                    if (existingProfile == null)
                    {
                        _profileManager.AddProfile(profile);
                        addedCount++;
                        processedIds.Add(profile.Id);
                        await LogSyncAction($"Added profile {profile.Name} (ID: {profile.Id})", clientIp);
                    }
                    else
                    {
                        _profileManager.UpdateProfile(profile);
                        updatedCount++;
                        processedIds.Add(profile.Id);
                        await LogSyncAction($"Updated profile {profile.Name} (ID: {profile.Id})", clientIp);
                    }
                }

                return (true, $"Processed {profiles.Count} profiles", addedCount, updatedCount, errorCount, processedIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving profile batch from {ClientIp}", clientIp);
                return (false, $"Error: {ex.Message}", 0, 0, profiles.Count, new List<int>());
            }
        }

        public async Task<(bool Success, string Message, int TotalCount, int AddedCount, int UpdatedCount, int ErrorCount)> ProcessFullSyncAsync(string jsonProfiles, string clientIp)
        {
            try
            {
                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(jsonProfiles);
                if (profiles == null || profiles.Count == 0)
                {
                    return (false, "No profiles to process", 0, 0, 0, 0);
                }

                int addedCount = 0;
                int updatedCount = 0;
                int errorCount = 0;

                foreach (var profile in profiles)
                {
                    var existingProfile = _profileManager.GetProfileById(profile.Id);
                    if (existingProfile == null)
                    {
                        _profileManager.AddProfile(profile);
                        addedCount++;
                        await LogSyncAction($"Added profile {profile.Name} (ID: {profile.Id})", clientIp);
                    }
                    else
                    {
                        _profileManager.UpdateProfile(profile);
                        updatedCount++;
                        await LogSyncAction($"Updated profile {profile.Name} (ID: {profile.Id})", clientIp);
                    }
                }

                return (true, $"Full sync completed: {profiles.Count} profiles processed", profiles.Count, addedCount, updatedCount, errorCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing full sync from {ClientIp}", clientIp);
                return (false, $"Error: {ex.Message}", 0, 0, 0, 0);
            }
        }

        public Dictionary<string, object> GetSyncStatus()
        {
            return new Dictionary<string, object>
            {
                { "LastSyncTime", DateTime.Now },
                { "ActiveConnections", 0 },
                { "TotalProfilesSynced", _profileManager.GetAllProfiles().Count }
            };
        }

        private async Task LogSyncAction(string message, string clientIp)
        {
            string logFile = Path.Combine(_logPath, $"silentsync_{DateTime.Now:yyyyMMdd}.log");
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {clientIp} - {message}{Environment.NewLine}";
            await File.AppendAllTextAsync(logFile, logEntry);
        }
    }
}