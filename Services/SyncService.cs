using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using System.Linq;
using System.Text.Json;
using System.Text;

namespace SteamCmdWeb.Services
{
    public class SyncService
    {
        private readonly ILogger<SyncService> _logger;
        private readonly ProfileService _profileService;
        private readonly List<SyncResult> _syncResults = new List<SyncResult>();
        private readonly List<string> _knownClients = new List<string>();
        private readonly object _syncLock = new object();

        // Danh sách profile đang chờ xác nhận
        private readonly List<ClientProfile> _pendingProfiles = new List<ClientProfile>();

        public SyncService(
            ILogger<SyncService> logger,
            ProfileService profileService)
        {
            _logger = logger;
            _profileService = profileService;
        }

        // Lấy danh sách kết quả đồng bộ
        public List<SyncResult> GetSyncResults()
        {
            lock (_syncLock)
            {
                return _syncResults.OrderByDescending(r => r.Timestamp).Take(100).ToList();
            }
        }

        // Lấy danh sách profile đang chờ xác nhận
        public List<ClientProfile> GetPendingProfiles()
        {
            lock (_syncLock)
            {
                return _pendingProfiles.ToList();
            }
        }

        // Xác nhận thêm profile vào danh sách chính
        public async Task<bool> ConfirmProfileAsync(int pendingIndex)
        {
            ClientProfile profileToAdd = null;

            lock (_syncLock)
            {
                if (pendingIndex < 0 || pendingIndex >= _pendingProfiles.Count)
                {
                    return false;
                }

                profileToAdd = _pendingProfiles[pendingIndex];
                _pendingProfiles.RemoveAt(pendingIndex);
            }

            if (profileToAdd != null)
            {
                await _profileService.AddProfileAsync(profileToAdd);
                _logger.LogInformation("Đã thêm profile {ProfileName} vào danh sách chính", profileToAdd.Name);
                return true;
            }

            return false;
        }

        // Từ chối thêm profile vào danh sách chính
        public bool RejectProfile(int pendingIndex)
        {
            lock (_syncLock)
            {
                if (pendingIndex < 0 || pendingIndex >= _pendingProfiles.Count)
                {
                    return false;
                }

                var profile = _pendingProfiles[pendingIndex];
                _pendingProfiles.RemoveAt(pendingIndex);
                _logger.LogInformation("Đã từ chối profile {ProfileName}", profile.Name);
                return true;
            }
        }

        // Xác nhận tất cả profile đang chờ
        public async Task<int> ConfirmAllPendingProfilesAsync()
        {
            List<ClientProfile> profilesToAdd;

            lock (_syncLock)
            {
                profilesToAdd = _pendingProfiles.ToList();
                _pendingProfiles.Clear();
            }

            int addedCount = 0;
            foreach (var profile in profilesToAdd)
            {
                try
                {
                    await _profileService.AddProfileAsync(profile);
                    addedCount++;
                    _logger.LogInformation("Đã thêm profile {ProfileName} vào danh sách chính", profile.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi thêm profile {ProfileName}", profile.Name);
                }
            }

            return addedCount;
        }

        // Từ chối tất cả profile đang chờ
        public int RejectAllPendingProfiles()
        {
            lock (_syncLock)
            {
                int count = _pendingProfiles.Count;
                _pendingProfiles.Clear();
                _logger.LogInformation("Đã từ chối {Count} profiles đang chờ", count);
                return count;
            }
        }

        // Quét tất cả máy trong mạng cục bộ
        public async Task DiscoverAndSyncClientsAsync()
        {
            _logger.LogInformation("Bắt đầu quét để tìm kiếm client...");
            var clients = await ScanNetworkAsync();
            _logger.LogInformation("Đã tìm thấy {Count} clients", clients.Count);

            foreach (var clientIp in clients)
            {
                if (!_knownClients.Contains(clientIp))
                {
                    _knownClients.Add(clientIp);
                }

                await SyncFromIpAsync(clientIp);
            }
        }

        // Tìm kiếm client có cổng TCP 61188 mở
        private async Task<List<string>> ScanNetworkAsync()
        {
            var result = new List<string>();
            try
            {
                // Lấy địa chỉ IP local
                string localIp = GetLocalIPAddress();
                if (string.IsNullOrEmpty(localIp))
                {
                    _logger.LogWarning("Không thể xác định địa chỉ IP local");
                    return result;
                }

                // Lấy 3 octet đầu của địa chỉ IP
                string baseIp = localIp.Substring(0, localIp.LastIndexOf('.') + 1);
                var scanTasks = new List<Task<string>>();

                // Quét từng địa chỉ IP trong subnet
                for (int i = 1; i <= 254; i++)
                {
                    string ip = baseIp + i;
                    scanTasks.Add(CheckPortAsync(ip, 61188));
                }

                // Đợi tất cả các task hoàn thành
                await Task.WhenAll(scanTasks);

                // Lọc kết quả
                foreach (var task in scanTasks)
                {
                    string clientIp = await task;
                    if (!string.IsNullOrEmpty(clientIp))
                    {
                        result.Add(clientIp);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi quét mạng");
            }

            return result;
        }

        // Kiểm tra cổng TCP có mở không
        private async Task<string> CheckPortAsync(string ip, int port, int timeout = 200)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(timeout);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == connectTask && client.Connected)
                {
                    _logger.LogInformation("Tìm thấy client tại {Ip}:{Port}", ip, port);
                    return ip;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // Đồng bộ từ tất cả client đã biết
        public async Task<List<SyncResult>> SyncFromAllKnownClientsAsync()
        {
            var results = new List<SyncResult>();

            foreach (var clientIp in _knownClients.ToList())
            {
                var result = await SyncFromIpAsync(clientIp);
                results.Add(result);
            }

            return results;
        }

        // Đồng bộ từ một địa chỉ IP cụ thể
        public async Task<SyncResult> SyncFromIpAsync(string ip, int port = 61188)
        {
            var result = new SyncResult
            {
                ClientId = ip,
                Success = false,
                Timestamp = DateTime.Now
            };

            try
            {
                _logger.LogInformation("Đang đồng bộ với client {Ip}:{Port}", ip, port);

                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(5000); // 5 giây timeout

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("Kết nối đến {Ip}:{Port} bị timeout", ip, port);
                    result.Message = "Kết nối bị timeout";
                    return SaveResult(result);
                }

                if (!client.Connected)
                {
                    _logger.LogWarning("Không thể kết nối đến {Ip}:{Port}", ip, port);
                    result.Message = "Không thể kết nối";
                    return SaveResult(result);
                }

                // Lấy danh sách profiles hiện tại
                var existingProfiles = await _profileService.GetAllProfilesAsync();
                var existingAppIds = existingProfiles.Select(p => p.AppID).ToHashSet();

                using var stream = client.GetStream();
                stream.ReadTimeout = 10000; // 10 giây
                stream.WriteTimeout = 10000; // 10 giây

                // Gửi lệnh GET_PROFILES
                string command = "AUTH:simple_auth_token GET_PROFILES";
                byte[] commandBytes = Encoding.UTF8.GetBytes(command);
                byte[] lengthBytes = BitConverter.GetBytes(commandBytes.Length);

                await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                await stream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await stream.FlushAsync();

                // Đọc phản hồi
                byte[] responseHeaderBuffer = new byte[4];
                int bytesRead = await stream.ReadAsync(responseHeaderBuffer, 0, 4);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Không đọc được phản hồi từ {Ip}:{Port}", ip, port);
                    result.Message = "Không nhận được phản hồi";
                    return SaveResult(result);
                }

                int responseLength = BitConverter.ToInt32(responseHeaderBuffer, 0);
                if (responseLength <= 0 || responseLength > 1024 * 1024) // Giới hạn 1MB
                {
                    _logger.LogWarning("Độ dài phản hồi không hợp lệ từ {Ip}:{Port}: {Length}", ip, port, responseLength);
                    result.Message = "Phản hồi không hợp lệ";
                    return SaveResult(result);
                }

                byte[] responseBuffer = new byte[responseLength];
                bytesRead = await stream.ReadAsync(responseBuffer, 0, responseLength);
                if (bytesRead < responseLength)
                {
                    _logger.LogWarning("Phản hồi không đầy đủ từ {Ip}:{Port}", ip, port);
                    result.Message = "Phản hồi không đầy đủ";
                    return SaveResult(result);
                }

                string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                if (response == "NO_PROFILES")
                {
                    _logger.LogInformation("Client {Ip}:{Port} không có profiles", ip, port);
                    result.Success = true;
                    result.Message = "Client không có profiles";
                    result.TotalProfiles = 0;
                    return SaveResult(result);
                }

                // Lấy danh sách tên profile
                var profileNames = response.Split(',');
                _logger.LogInformation("Nhận {Count} profiles từ {Ip}:{Port}", profileNames.Length, ip, port);

                result.TotalProfiles = profileNames.Length;
                int added = 0;
                int filtered = 0;

                // Lấy chi tiết từng profile
                foreach (var profileName in profileNames)
                {
                    try
                    {
                        // Gửi lệnh GET_PROFILE_DETAILS
                        command = $"AUTH:simple_auth_token GET_PROFILE_DETAILS {profileName}";
                        commandBytes = Encoding.UTF8.GetBytes(command);
                        lengthBytes = BitConverter.GetBytes(commandBytes.Length);

                        await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                        await stream.WriteAsync(commandBytes, 0, commandBytes.Length);
                        await stream.FlushAsync();

                        // Đọc phản hồi
                        bytesRead = await stream.ReadAsync(responseHeaderBuffer, 0, 4);
                        if (bytesRead < 4)
                        {
                            _logger.LogWarning("Không đọc được phản hồi chi tiết profile từ {Ip}:{Port}", ip, port);
                            continue;
                        }

                        responseLength = BitConverter.ToInt32(responseHeaderBuffer, 0);
                        if (responseLength <= 0 || responseLength > 5 * 1024 * 1024) // Giới hạn 5MB
                        {
                            _logger.LogWarning("Độ dài phản hồi chi tiết profile không hợp lệ từ {Ip}:{Port}: {Length}",
                                ip, port, responseLength);
                            continue;
                        }

                        responseBuffer = new byte[responseLength];
                        bytesRead = await stream.ReadAsync(responseBuffer, 0, responseLength);
                        if (bytesRead < responseLength)
                        {
                            _logger.LogWarning("Phản hồi chi tiết profile không đầy đủ từ {Ip}:{Port}", ip, port);
                            continue;
                        }

                        response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                        if (response == "PROFILE_NOT_FOUND")
                        {
                            _logger.LogWarning("Profile {ProfileName} không tìm thấy trên client {Ip}:{Port}",
                                profileName, ip, port);
                            continue;
                        }

                        // Chuyển đổi JSON thành SteamCmdProfile
                        var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(response);
                        if (steamCmdProfile == null)
                        {
                            _logger.LogWarning("Không thể chuyển đổi dữ liệu profile {ProfileName} từ {Ip}:{Port}",
                                profileName, ip, port);
                            continue;
                        }

                        // Kiểm tra AppID đã tồn tại chưa
                        if (existingAppIds.Contains(steamCmdProfile.AppID))
                        {
                            _logger.LogInformation("Profile {ProfileName} (AppID: {AppID}) đã tồn tại, bỏ qua",
                                steamCmdProfile.Name, steamCmdProfile.AppID);
                            filtered++;
                            continue;
                        }

                        // Thêm vào danh sách chờ xác nhận thay vì thêm trực tiếp
                        var clientProfile = new ClientProfile
                        {
                            Name = steamCmdProfile.Name,
                            AppID = steamCmdProfile.AppID,
                            InstallDirectory = steamCmdProfile.InstallDirectory,
                            SteamUsername = steamCmdProfile.SteamUsername,
                            SteamPassword = steamCmdProfile.SteamPassword,
                            Arguments = steamCmdProfile.Arguments,
                            ValidateFiles = steamCmdProfile.ValidateFiles,
                            AutoRun = steamCmdProfile.AutoRun,
                            AnonymousLogin = steamCmdProfile.AnonymousLogin,
                            Status = "Ready",
                            StartTime = DateTime.Now,
                            StopTime = DateTime.Now,
                            LastRun = DateTime.UtcNow
                        };

                        // Thêm vào danh sách chờ
                        lock (_syncLock)
                        {
                            _pendingProfiles.Add(clientProfile);
                        }

                        added++;
                        _logger.LogInformation("Đã thêm profile {ProfileName} (AppID: {AppID}) từ {Ip}:{Port} vào danh sách chờ",
                            clientProfile.Name, clientProfile.AppID, ip, port);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile {ProfileName} từ {Ip}:{Port}",
                            profileName, ip, port);
                    }
                }

                result.Success = true;
                result.NewProfilesAdded = added;
                result.FilteredProfiles = filtered;
                result.Message = $"Đã thêm {added} profiles vào danh sách chờ, bỏ qua {filtered} profiles";
                _logger.LogInformation("Đồng bộ với {Ip}:{Port} hoàn tất: {Added} thêm vào danh sách chờ, {Filtered} bỏ qua",
                    ip, port, added, filtered);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ với client {Ip}:{Port}", ip, port);
                result.Message = $"Lỗi: {ex.Message}";
            }

            return SaveResult(result);
        }

        // Lưu kết quả đồng bộ
        private SyncResult SaveResult(SyncResult result)
        {
            lock (_syncLock)
            {
                _syncResults.Add(result);
                // Giới hạn số lượng kết quả
                if (_syncResults.Count > 1000)
                {
                    _syncResults.RemoveRange(0, _syncResults.Count - 1000);
                }
            }
            return result;
        }

        // Lấy địa chỉ IP local
        private string GetLocalIPAddress()
        {
            try
            {
                string hostName = Dns.GetHostName();
                var host = Dns.GetHostEntry(hostName);
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }

                // Cách khác nếu cách trên không hoạt động
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up &&
                           i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                foreach (var adapter in networkInterfaces)
                {
                    var properties = adapter.GetIPProperties();
                    foreach (var address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return address.Address.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy địa chỉ IP local");
            }

            return string.Empty;
        }
    }
}