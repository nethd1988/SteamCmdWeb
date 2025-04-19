using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using System.Collections.Concurrent;

namespace SteamCmdWeb.Services
{
    public class SyncService
    {
        private readonly ILogger<SyncService> _logger;
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;

        private static readonly ConcurrentBag<ClientProfile> _pendingProfiles = new ConcurrentBag<ClientProfile>();
        private static readonly ConcurrentBag<SyncResult> _syncResults = new ConcurrentBag<SyncResult>();

        // Giới hạn số lượng kết quả lưu trữ
        private const int MaxResultsCount = 100;

        public SyncService(
            ILogger<SyncService> logger,
            ProfileService profileService,
            DecryptionService decryptionService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
        }

        // Added method for TcpServerService
        public List<ClientProfile> GetAllProfiles()
        {
            return _profileService.GetAllProfilesAsync().GetAwaiter().GetResult();
        }

        // Added method for TcpServerService
        public ClientProfile GetProfileByName(string name)
        {
            var profiles = GetAllProfiles();
            return profiles.FirstOrDefault(p => p.Name == name);
        }

        public List<ClientProfile> GetPendingProfiles()
        {
            return _pendingProfiles.ToList();
        }

        public void AddPendingProfile(ClientProfile profile)
        {
            // Giải mã thông tin đăng nhập nếu có
            if (!profile.AnonymousLogin && !string.IsNullOrEmpty(profile.SteamUsername))
            {
                try
                {
                    profile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                }
                catch
                {
                    // Để nguyên nếu không giải mã được
                }
            }

            if (!profile.AnonymousLogin && !string.IsNullOrEmpty(profile.SteamPassword))
            {
                try
                {
                    profile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                }
                catch
                {
                    // Để nguyên nếu không giải mã được
                }
            }

            _pendingProfiles.Add(profile);
        }

        public async Task<bool> ConfirmProfileAsync(int index)
        {
            if (index < 0 || index >= _pendingProfiles.Count)
            {
                return false;
            }

            var profileToAdd = _pendingProfiles.ElementAt(index);
            if (!_pendingProfiles.TryTake(out var _)) // Remove the profile (ConcurrentBag doesn't support direct index removal)
            {
                return false;
            }

            // Đảm bảo mã hóa thông tin đăng nhập trước khi lưu
            if (!profileToAdd.AnonymousLogin)
            {
                if (!string.IsNullOrEmpty(profileToAdd.SteamUsername))
                {
                    profileToAdd.SteamUsername = _decryptionService.EncryptString(profileToAdd.SteamUsername);
                }

                if (!string.IsNullOrEmpty(profileToAdd.SteamPassword))
                {
                    profileToAdd.SteamPassword = _decryptionService.EncryptString(profileToAdd.SteamPassword);
                }
            }

            await _profileService.AddProfileAsync(profileToAdd);
            return true;
        }

        public bool RejectProfile(int index)
        {
            if (index < 0 || index >= _pendingProfiles.Count)
            {
                return false;
            }

            return _pendingProfiles.TryTake(out var _);
        }

        public async Task<int> ConfirmAllPendingProfilesAsync()
        {
            var profilesToAdd = _pendingProfiles.ToList();
            _pendingProfiles.Clear();

            if (profilesToAdd.Count == 0)
            {
                return 0;
            }

            int addedCount = 0;
            foreach (var profile in profilesToAdd)
            {
                try
                {
                    // Mã hóa thông tin đăng nhập
                    if (!profile.AnonymousLogin)
                    {
                        if (!string.IsNullOrEmpty(profile.SteamUsername))
                        {
                            profile.SteamUsername = _decryptionService.EncryptString(profile.SteamUsername);
                        }

                        if (!string.IsNullOrEmpty(profile.SteamPassword))
                        {
                            profile.SteamPassword = _decryptionService.EncryptString(profile.SteamPassword);
                        }
                    }

                    await _profileService.AddProfileAsync(profile);
                    addedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi thêm profile {ProfileName}", profile.Name);
                }
            }

            return addedCount;
        }

        public int RejectAllPendingProfiles()
        {
            int count = _pendingProfiles.Count;
            _pendingProfiles.Clear();
            return count;
        }

        public void AddSyncResult(SyncResult result)
        {
            _syncResults.Add(result);

            // Giới hạn số lượng kết quả
            while (_syncResults.Count > MaxResultsCount)
            {
                _syncResults.TryTake(out var _);
            }
        }

        public List<SyncResult> GetSyncResults()
        {
            return _syncResults.OrderByDescending(r => r.Timestamp).ToList();
        }

        public async Task<List<SyncResult>> SyncFromAllKnownClientsAsync()
        {
            var results = new List<SyncResult>();
            var clientRegistrations = await LoadClientRegistrationsAsync();

            foreach (var client in clientRegistrations)
            {
                try
                {
                    var result = await SyncFromClientAsync(client.Address, client.Port);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi đồng bộ từ client {ClientId} ({Address})", client.ClientId, client.Address);
                    results.Add(new SyncResult
                    {
                        ClientId = client.ClientId,
                        Success = false,
                        Message = $"Lỗi: {ex.Message}",
                        Timestamp = DateTime.Now
                    });
                }
            }

            return results;
        }

        public async Task DiscoverAndSyncClientsAsync()
        {
            var localIPs = GetLocalIPAddresses();

            foreach (var ip in localIPs)
            {
                var subnet = GetSubnetFromIP(ip);
                await ScanSubnetAsync(subnet);
            }
        }

        private async Task ScanSubnetAsync(string subnet)
        {
            var tasks = new List<Task>();

            for (int i = 1; i <= 254; i++)
            {
                string ipToCheck = $"{subnet}.{i}";
                tasks.Add(CheckAndSyncFromIPAsync(ipToCheck));

                if (tasks.Count >= 20)
                {
                    await Task.WhenAny(tasks);
                    tasks.RemoveAll(t => t.IsCompleted);
                }
            }

            await Task.WhenAll(tasks);
        }

        private async Task CheckAndSyncFromIPAsync(string ip)
        {
            if (await IsSteamCmdWebAPIRunningAsync(ip))
            {
                try
                {
                    await SyncFromClientAsync(ip, 61188);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể đồng bộ từ {IP}", ip);
                }
            }
        }

        private async Task<bool> IsSteamCmdWebAPIRunningAsync(string ip)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(ip, 61188);
                    await Task.WhenAny(connectTask, Task.Delay(500));

                    if (client.Connected)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi
            }

            return false;
        }

        private string GetSubnetFromIP(string ip)
        {
            var parts = ip.Split('.');
            if (parts.Length == 4)
            {
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
            }
            return "192.168.1";
        }

        private List<string> GetLocalIPAddresses()
        {
            var result = new List<string>();

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up);

                foreach (var iface in interfaces)
                {
                    var ipProps = iface.GetIPProperties();

                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            result.Add(addr.Address.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách địa chỉ IP cục bộ");
                // Thêm các địa chỉ mạng cục bộ phổ biến
                result.Add("192.168.1.1");
                result.Add("192.168.0.1");
                result.Add("10.0.0.1");
            }

            return result;
        }

        public async Task<SyncResult> SyncFromClientAsync(string address, int port = 61188)
        {
            try
            {
                _logger.LogInformation("Đang đồng bộ từ client {Address}:{Port}", address, port);

                using (var tcpClient = new TcpClient())
                {
                    var connectTask = tcpClient.ConnectAsync(address, port);
                    await Task.WhenAny(connectTask, Task.Delay(3000));

                    if (!tcpClient.Connected)
                    {
                        return new SyncResult
                        {
                            ClientId = address,
                            Success = false,
                            Message = "Không thể kết nối đến client",
                            Timestamp = DateTime.Now
                        };
                    }

                    using (var stream = tcpClient.GetStream())
                    {
                        // Gửi lệnh yêu cầu danh sách profile với authentication
                        string command = "AUTH:simple_auth_token GET_PROFILES"; // Added authentication
                        byte[] commandBytes = System.Text.Encoding.UTF8.GetBytes(command);

                        // Gửi độ dài trước
                        byte[] lengthBytes = BitConverter.GetBytes(commandBytes.Length);
                        await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);

                        // Gửi lệnh
                        await stream.WriteAsync(commandBytes, 0, commandBytes.Length);
                        await stream.FlushAsync();

                        // Đọc phản hồi
                        byte[] headerBuffer = new byte[4];
                        int bytesRead = await stream.ReadAsync(headerBuffer, 0, 4);
                        if (bytesRead < 4)
                        {
                            return new SyncResult
                            {
                                ClientId = address,
                                Success = false,
                                Message = "Không nhận được phản hồi từ client",
                                Timestamp = DateTime.Now
                            };
                        }

                        int responseLength = BitConverter.ToInt32(headerBuffer, 0);
                        if (responseLength <= 0 || responseLength > 10 * 1024 * 1024) // Giới hạn 10MB
                        {
                            return new SyncResult
                            {
                                ClientId = address,
                                Success = false,
                                Message = $"Độ dài phản hồi không hợp lệ: {responseLength}",
                                Timestamp = DateTime.Now
                            };
                        }

                        byte[] responseBuffer = new byte[responseLength];
                        bytesRead = await stream.ReadAsync(responseBuffer, 0, responseLength);

                        if (bytesRead < responseLength)
                        {
                            return new SyncResult
                            {
                                ClientId = address,
                                Success = false,
                                Message = "Phản hồi không đầy đủ",
                                Timestamp = DateTime.Now
                            };
                        }

                        string response = System.Text.Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                        if (response.StartsWith("ERROR:"))
                        {
                            return new SyncResult
                            {
                                ClientId = address,
                                Success = false,
                                Message = response,
                                Timestamp = DateTime.Now
                            };
                        }

                        // Phân tích JSON
                        var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(response);

                        // Lọc profiles đã tồn tại
                        var existingProfiles = await _profileService.GetAllProfilesAsync();
                        var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                        int newProfilesCount = 0;
                        int filteredCount = 0;

                        foreach (var profile in profiles)
                        {
                            if (existingAppIds.Contains(profile.AppID))
                            {
                                filteredCount++;
                                continue;
                            }

                            // Giải mã thông tin đăng nhập
                            if (!profile.AnonymousLogin)
                            {
                                if (!string.IsNullOrEmpty(profile.SteamUsername))
                                {
                                    try
                                    {
                                        profile.SteamUsername = _decryptionService.DecryptString(profile.SteamUsername);
                                    }
                                    catch
                                    {
                                        // Để nguyên nếu không giải mã được
                                    }
                                }

                                if (!string.IsNullOrEmpty(profile.SteamPassword))
                                {
                                    try
                                    {
                                        profile.SteamPassword = _decryptionService.DecryptString(profile.SteamPassword);
                                    }
                                    catch
                                    {
                                        // Để nguyên nếu không giải mã được
                                    }
                                }
                            }

                            AddPendingProfile(profile);
                            newProfilesCount++;
                        }

                        // Tạo kết quả đồng bộ
                        var result = new SyncResult
                        {
                            ClientId = address,
                            Success = true,
                            Message = $"Đồng bộ thành công: {newProfilesCount} profile mới, {filteredCount} profile đã lọc",
                            TotalProfiles = profiles.Count,
                            NewProfilesAdded = newProfilesCount,
                            FilteredProfiles = filteredCount,
                            Timestamp = DateTime.Now
                        };

                        AddSyncResult(result);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ client {Address}:{Port}", address, port);

                var result = new SyncResult
                {
                    ClientId = address,
                    Success = false,
                    Message = $"Lỗi: {ex.Message}",
                    Timestamp = DateTime.Now
                };

                AddSyncResult(result);
                return result;
            }
        }

        private async Task<List<ClientRegistration>> LoadClientRegistrationsAsync()
        {
            try
            {
                var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
                var filePath = Path.Combine(dataFolder, "ClientRegistrations.json");

                if (!File.Exists(filePath))
                {
                    return new List<ClientRegistration>();
                }

                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<List<ClientRegistration>>(json) ?? new List<ClientRegistration>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc danh sách client đã đăng ký");
                return new List<ClientRegistration>();
            }
        }

        public async Task<SyncResult> SyncFromIpAsync(string ip, int port = 61188)
        {
            return await SyncFromClientAsync(ip, port);
        }
    }
}