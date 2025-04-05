using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;

namespace SteamCmdWeb.Services
{
    public class ProfileMigrationService
    {
        private readonly ILogger<ProfileMigrationService> _logger;
        private readonly AppProfileManager _appProfileManager;
        private readonly string _backupFolder;

        public ProfileMigrationService(ILogger<ProfileMigrationService> logger, AppProfileManager appProfileManager)
        {
            _logger = logger;
            _appProfileManager = appProfileManager;

            _backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backup");
            if (!Directory.Exists(_backupFolder))
            {
                Directory.CreateDirectory(_backupFolder);
                _logger.LogInformation("Created backup directory at {Path}", _backupFolder);
            }
        }

        public async Task<(int Added, int Skipped)> MigrateProfilesToAppProfiles(List<ClientProfile> profiles, bool skipDuplicateCheck = false)
        {
            int added = 0;
            int skipped = 0;

            if (profiles == null || profiles.Count == 0)
            {
                return (added, skipped);
            }

            // Get existing app profiles for comparison
            var existingProfiles = _appProfileManager.GetAllProfiles();

            foreach (var profile in profiles)
            {
                try
                {
                    // Check if profile already exists (by ID or by name)
                    bool exists = existingProfiles.Any(p => p.Id == profile.Id ||
                                                          (p.Name == profile.Name && p.AppID == profile.AppID));

                    // Check for duplicate credentials (same username and password)
                    bool hasDuplicateCredentials = false;
                    if (!skipDuplicateCheck && !string.IsNullOrEmpty(profile.SteamUsername) && !string.IsNullOrEmpty(profile.SteamPassword))
                    {
                        hasDuplicateCredentials = existingProfiles.Any(p =>
                            p.SteamUsername == profile.SteamUsername &&
                            p.SteamPassword == profile.SteamPassword);
                    }

                    // If the profile exists but credentials don't, add it anyway
                    if (exists && hasDuplicateCredentials)
                    {
                        _logger.LogInformation("Skipping profile {Name} (ID: {Id}) - duplicate credentials",
                            profile.Name, profile.Id);
                        skipped++;
                    }
                    else
                    {
                        // Add the profile, ensuring it gets a new ID if needed
                        if (exists)
                        {
                            // If game is duplicate but credentials aren't, assign new ID
                            profile.Id = 0; // AppProfileManager will assign a new ID
                        }

                        _appProfileManager.AddProfile(profile);
                        added++;

                        _logger.LogInformation("Added profile {Name} (ID: {Id}) to AppProfiles",
                            profile.Name, profile.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding profile {Name} (ID: {Id}) to AppProfiles",
                        profile.Name, profile.Id);
                    skipped++;
                }
            }

            return (added, skipped);
        }

        public async Task<string> BackupClientProfiles(List<ClientProfile> profiles)
        {
            try
            {
                if (profiles == null || profiles.Count == 0)
                {
                    return "No profiles to backup";
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"client_backup_{timestamp}.json";
                string filePath = Path.Combine(_backupFolder, fileName);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonContent = JsonSerializer.Serialize(profiles, options);

                await File.WriteAllTextAsync(filePath, jsonContent);

                _logger.LogInformation("Backed up {Count} profiles to {FilePath}", profiles.Count, filePath);

                return $"Successfully backed up {profiles.Count} profiles to {fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error backing up client profiles");
                return $"Error backing up profiles: {ex.Message}";
            }
        }

        public List<BackupInfo> GetBackupFiles()
        {
            try
            {
                var backupFiles = new DirectoryInfo(_backupFolder)
                    .GetFiles("*.json")
                    .OrderByDescending(f => f.CreationTime)
                    .Take(50) // Limit to 50 most recent backups
                    .Select(f => new BackupInfo
                    {
                        FileName = f.Name,
                        CreationTime = f.CreationTime,
                        SizeMB = Math.Round(f.Length / (1024.0 * 1024), 2),
                        FullPath = f.FullName
                    })
                    .ToList();

                return backupFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting backup files");
                return new List<BackupInfo>();
            }
        }

        public async Task<List<ClientProfile>> LoadProfilesFromBackup(string fileName)
        {
            try
            {
                string filePath = Path.Combine(_backupFolder, fileName);

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Backup file not found: {FilePath}", filePath);
                    return new List<ClientProfile>();
                }

                string jsonContent = await File.ReadAllTextAsync(filePath);

                // Thêm xử lý linh hoạt hơn với JsonSerializerOptions
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(jsonContent, options);

                if (profiles == null)
                {
                    _logger.LogWarning("Failed to deserialize backup file: {FileName}", fileName);
                    return new List<ClientProfile>();
                }

                _logger.LogInformation("Loaded {Count} profiles from backup file {FileName}",
                    profiles.Count, fileName);

                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading profiles from backup file {FileName}", fileName);
                throw; // Để controller xử lý ngoại lệ
            }
        }
    }

    public class BackupInfo
    {
        public string FileName { get; set; }
        public DateTime CreationTime { get; set; }
        public double SizeMB { get; set; }
        public string FullPath { get; set; }
    }
}