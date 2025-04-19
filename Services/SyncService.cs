using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Models;
using SteamCmdWebAPI.Models;

namespace SteamCmdWeb.Services
{
    public class SyncService
    {
        private readonly ILogger<SyncService> _logger;
        private readonly ProfileService _profileService;
        private readonly DecryptionService _decryptionService;
        private readonly string _syncFolder;
        private readonly object _syncLock = new object();
        private readonly List<ClientProfile> _pendingProfiles = new List<ClientProfile>();
        private readonly List<SyncResult> _syncResults = new List<SyncResult>();
        private readonly int _maxSyncResults = 100;

        public SyncService(
            ILogger<SyncService> logger,
            ProfileService profileService,
            DecryptionService decryptionService)
        {
            _logger = logger;
            _profileService = profileService;
            _decryptionService = decryptionService;

            // Đường dẫn đến thư mục đồng bộ
            string dataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            _syncFolder = Path.Combine(dataFolder, "Sync");

            if (!Directory.Exists(_syncFolder))
            {
                Directory.CreateDirectory(_syncFolder);
            }

            // Tải danh sách profiles chờ từ file nếu có
            LoadPendingProfiles();
        }

        private void LoadPendingProfiles()
        {
            try
            {
                string pendingProfilesPath = Path.Combine(_syncFolder, "pending_profiles.json");
                if (File.Exists(pendingProfilesPath))
                {
                    string json = File.ReadAllText(pendingProfilesPath);
                    var profiles = JsonSerializer.Deserialize<List<ClientProfile>>(json);
                    if (profiles != null && profiles.Count > 0)
                    {
                        lock (_syncLock)
                        {
                            _pendingProfiles.Clear();
                            _pendingProfiles.AddRange(profiles);
                        }
                        _logger.LogInformation("Đã tải {Count} profiles chờ từ file", profiles.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tải danh sách profiles chờ");
            }
        }

        private void SavePendingProfiles()
        {
            try
            {
                string pendingProfilesPath = Path.Combine(_syncFolder, "pending_profiles.json");
                List<ClientProfile> profiles;

                lock (_syncLock)
                {
                    profiles = new List<ClientProfile>(_pendingProfiles);
                }

                string json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(pendingProfilesPath, json);
                _logger.LogInformation("Đã lưu {Count} profiles chờ vào file", profiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu danh sách profiles chờ");
            }
        }

        // Thêm kết quả đồng bộ
        public void AddSyncResult(SyncResult result)
        {
            lock (_syncLock)
            {
                _syncResults.Add(result);

                // Giữ số lượng kết quả không vượt quá giới hạn
                if (_syncResults.Count > _maxSyncResults)
                {
                    _syncResults.RemoveRange(0, _syncResults.Count - _maxSyncResults);
                }
            }
        }

        // Lấy danh sách kết quả đồng bộ
        public List<SyncResult> GetSyncResults()
        {
            lock (_syncLock)
            {
                return new List<SyncResult>(_syncResults.OrderByDescending(r => r.Timestamp));
            }
        }

        // Lấy danh sách profile chờ xác nhận
        public List<ClientProfile> GetPendingProfiles()
        {
            lock (_syncLock)
            {
                return _pendingProfiles;
            }
        }

        // Thêm profile vào danh sách chờ
        public void AddPendingProfile(ClientProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            lock (_syncLock)
            {
                // Kiểm tra trùng lặp
                bool isDuplicate = _pendingProfiles.Any(p => p.AppID == profile.AppID || p.Name == profile.Name);

                if (!isDuplicate)
                {
                    _pendingProfiles.Add(profile);
                    _logger.LogInformation("Đã thêm profile {ProfileName} vào danh sách chờ", profile.Name);
                    SavePendingProfiles();
                }
                else
                {
                    _logger.LogWarning("Profile {ProfileName} đã tồn tại trong danh sách chờ", profile.Name);
                }
            }
        }

        // Xác nhận thêm một profile vào hệ thống
        public async Task<bool> ConfirmProfileAsync(int index)
        {
            ClientProfile profile = null;

            lock (_syncLock)
            {
                if (index < 0 || index >= _pendingProfiles.Count)
                {
                    return false;
                }

                profile = _pendingProfiles[index];
                _pendingProfiles.RemoveAt(index);
                SavePendingProfiles();
            }

            if (profile != null)
            {
                await _profileService.AddProfileAsync(profile);
                _logger.LogInformation("Đã xác nhận và thêm profile {ProfileName} vào hệ thống", profile.Name);
                return true;
            }

            return false;
        }

        // Từ chối một profile
        public bool RejectProfile(int index)
        {
            lock (_syncLock)
            {
                if (index < 0 || index >= _pendingProfiles.Count)
                {
                    return false;
                }

                var profile = _pendingProfiles[index];
                _pendingProfiles.RemoveAt(index);
                SavePendingProfiles();

                _logger.LogInformation("Đã từ chối profile {ProfileName}", profile.Name);
                return true;
            }
        }

        // Xác nhận tất cả các profile chờ
        public async Task<int> ConfirmAllPendingProfilesAsync()
        {
            List<ClientProfile> profiles;

            lock (_syncLock)
            {
                profiles = new List<ClientProfile>(_pendingProfiles);
                _pendingProfiles.Clear();
                SavePendingProfiles();
            }

            int count = 0;
            foreach (var profile in profiles)
            {
                try
                {
                    await _profileService.AddProfileAsync(profile);
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi thêm profile {ProfileName}", profile.Name);
                }
            }

            _logger.LogInformation("Đã xác nhận và thêm {Count} profiles vào hệ thống", count);
            return count;
        }

        // Từ chối tất cả các profile chờ
        public int RejectAllPendingProfiles()
        {
            lock (_syncLock)
            {
                int count = _pendingProfiles.Count;
                _pendingProfiles.Clear();
                SavePendingProfiles();

                _logger.LogInformation("Đã từ chối {Count} profiles", count);
                return count;
            }
        }

        // Tìm và đồng bộ với tất cả client trên mạng
        public async Task DiscoverAndSyncClientsAsync()
        {
            _logger.LogInformation("Bắt đầu tìm kiếm và đồng bộ với các client");

            // Thêm logic tìm kiếm client trên mạng ở đây
            List<string> discoveredIps = await DiscoverClientsOnNetwork();

            int successCount = 0;
            int totalProfilesAdded = 0;

            foreach (var ip in discoveredIps)
            {
                try
                {
                    var result = await SyncFromIpAsync(ip);
                    if (result.Success)
                    {
                        successCount++;
                        totalProfilesAdded += result.NewProfilesAdded;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi đồng bộ từ IP {IpAddress}", ip);
                }
            }

            var summaryResult = new SyncResult
            {
                ClientId = "auto-discovery",
                Success = discoveredIps.Count > 0,
                Message = $"Hoàn thành quét mạng tự động. Tìm thấy {discoveredIps.Count} clients, đồng bộ thành công {successCount} clients",
                TotalProfiles = totalProfilesAdded,
                NewProfilesAdded = totalProfilesAdded,
                FilteredProfiles = 0,
                Timestamp = DateTime.Now
            };

            AddSyncResult(summaryResult);
        }

        // Phương thức phát hiện client trên mạng
        private async Task<List<string>> DiscoverClientsOnNetwork()
        {
            var discoveredIps = new List<string>();

            try
            {
                // Lấy địa chỉ IP của máy local
                string localIp = GetLocalIPAddress();
                if (string.IsNullOrEmpty(localIp))
                {
                    _logger.LogWarning("Không thể xác định địa chỉ IP local");
                    return discoveredIps;
                }

                // Lấy phần prefix của địa chỉ IP (ví dụ: 192.168.1.*)
                string ipPrefix = localIp.Substring(0, localIp.LastIndexOf('.') + 1);

                // Quét các địa chỉ IP trong mạng
                List<Task<Tuple<string, bool>>> checkTasks = new List<Task<Tuple<string, bool>>>();

                for (int i = 1; i <= 254; i++)
                {
                    string ip = ipPrefix + i;
                    if (ip == localIp) continue; // Bỏ qua địa chỉ IP của máy local

                    checkTasks.Add(CheckClientAsync(ip));
                }

                // Chờ tất cả các task hoàn thành
                var results = await Task.WhenAll(checkTasks);

                // Lọc ra các địa chỉ IP có client
                foreach (var result in results)
                {
                    if (result.Item2)
                    {
                        discoveredIps.Add(result.Item1);
                    }
                }

                _logger.LogInformation("Tìm thấy {Count} clients trên mạng", discoveredIps.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tìm kiếm client trên mạng");
            }

            return discoveredIps;
        }

        // Kiểm tra xem một địa chỉ IP có chạy client hay không
        private async Task<Tuple<string, bool>> CheckClientAsync(string ipAddress)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    // Cố gắng kết nối đến địa chỉ IP với cổng 61188
                    var connectTask = client.ConnectAsync(ipAddress, 61188);
                    var timeoutTask = Task.Delay(500); // Timeout 500ms

                    if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
                    {
                        // Kết nối thành công
                        return new Tuple<string, bool>(ipAddress, true);
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi kết nối
            }

            return new Tuple<string, bool>(ipAddress, false);
        }

        // Đồng bộ từ một máy client cụ thể
        public async Task<SyncResult> SyncFromIpAsync(string ipAddress, int port = 61188)
        {
            _logger.LogInformation("Bắt đầu đồng bộ từ địa chỉ IP {IpAddress}:{Port}", ipAddress, port);

            try
            {
                // Tạo một kết quả mặc định
                var result = new SyncResult
                {
                    ClientId = ipAddress,
                    Success = false,
                    Message = "Chưa thể kết nối với client",
                    Timestamp = DateTime.Now
                };

                using (var client = new TcpClient())
                {
                    // Cố gắng kết nối đến client
                    var connectTask = client.ConnectAsync(ipAddress, port);
                    var timeoutTask = Task.Delay(5000); // Timeout 5 giây

                    if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                    {
                        // Kết nối bị timeout
                        result.Message = "Kết nối bị timeout";
                        AddSyncResult(result);
                        return result;
                    }

                    if (!client.Connected)
                    {
                        // Không thể kết nối
                        result.Message = "Không thể kết nối đến client";
                        AddSyncResult(result);
                        return result;
                    }

                    // Kết nối thành công, gửi yêu cầu đồng bộ
                    NetworkStream stream = client.GetStream();

                    // Gửi lệnh AUTH + GET_PROFILES
                    string command = "AUTH:simple_auth_token GET_PROFILES";
                    byte[] commandBytes = Encoding.UTF8.GetBytes(command);
                    byte[] lengthBytes = BitConverter.GetBytes(commandBytes.Length);

                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                    await stream.WriteAsync(commandBytes, 0, commandBytes.Length);

                    // Đọc độ dài phản hồi
                    byte[] headerBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(headerBuffer, 0, 4);
                    if (bytesRead != 4)
                    {
                        result.Message = "Không thể đọc phản hồi từ client";
                        AddSyncResult(result);
                        return result;
                    }

                    int responseLength = BitConverter.ToInt32(headerBuffer, 0);
                    if (responseLength <= 0 || responseLength > 1024 * 1024) // Giới hạn 1MB
                    {
                        result.Message = "Độ dài phản hồi không hợp lệ";
                        AddSyncResult(result);
                        return result;
                    }

                    // Đọc nội dung phản hồi
                    byte[] responseBuffer = new byte[responseLength];
                    bytesRead = await stream.ReadAsync(responseBuffer, 0, responseLength);

                    if (bytesRead != responseLength)
                    {
                        result.Message = "Phản hồi không đầy đủ";
                        AddSyncResult(result);
                        return result;
                    }

                    string response = Encoding.UTF8.GetString(responseBuffer);

                    if (response == "NO_PROFILES")
                    {
                        result.Success = true;
                        result.Message = "Không có profiles trên client";
                        result.TotalProfiles = 0;
                        AddSyncResult(result);
                        return result;
                    }

                    // Phân tích danh sách tên profile
                    string[] profileNames = response.Split(',');

                    // Đếm số profile được đồng bộ
                    int totalProfiles = profileNames.Length;
                    int newProfilesAdded = 0;

                    // Lấy chi tiết từng profile và thêm vào danh sách chờ
                    foreach (var profileName in profileNames)
                    {
                        try
                        {
                            // Gửi lệnh lấy chi tiết profile
                            command = $"AUTH:simple_auth_token GET_PROFILE_DETAILS {profileName}";
                            commandBytes = Encoding.UTF8.GetBytes(command);
                            lengthBytes = BitConverter.GetBytes(commandBytes.Length);

                            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                            await stream.WriteAsync(commandBytes, 0, commandBytes.Length);

                            // Đọc độ dài phản hồi
                            bytesRead = await stream.ReadAsync(headerBuffer, 0, 4);
                            if (bytesRead != 4) continue;

                            responseLength = BitConverter.ToInt32(headerBuffer, 0);
                            if (responseLength <= 0 || responseLength > 1024 * 1024) continue;

                            // Đọc nội dung phản hồi
                            responseBuffer = new byte[responseLength];
                            bytesRead = await stream.ReadAsync(responseBuffer, 0, responseLength);

                            if (bytesRead != responseLength) continue;

                            response = Encoding.UTF8.GetString(responseBuffer);

                            if (response == "PROFILE_NOT_FOUND") continue;

                            // Phân tích profile
                            var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(response);

                            // Chuyển đổi sang ClientProfile
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
                            AddPendingProfile(clientProfile);
                            newProfilesAdded++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Lỗi khi lấy chi tiết profile {ProfileName} từ {IpAddress}", profileName, ipAddress);
                        }
                    }

                    // Cập nhật kết quả
                    result.Success = true;
                    result.Message = $"Đồng bộ thành công từ {ipAddress}. Đã thêm {newProfilesAdded}/{totalProfiles} profiles";
                    result.TotalProfiles = totalProfiles;
                    result.NewProfilesAdded = newProfilesAdded;
                    result.FilteredProfiles = totalProfiles - newProfilesAdded;
                }

                // Lưu kết quả
                AddSyncResult(result);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đồng bộ từ địa chỉ IP {IpAddress}:{Port}", ipAddress, port);

                var failedResult = new SyncResult
                {
                    ClientId = ipAddress,
                    Success = false,
                    Message = "Lỗi: " + ex.Message,
                    Timestamp = DateTime.Now
                };

                AddSyncResult(failedResult);

                return failedResult;
            }
        }

        // Lấy địa chỉ IP local
        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }

                // Nếu không tìm thấy, thử cách khác
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    return endPoint.Address.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy địa chỉ IP local");
                return string.Empty;
            }
        }

        // Đồng bộ từ tất cả client đã biết
        public async Task<List<SyncResult>> SyncFromAllKnownClientsAsync()
        {
            _logger.LogInformation("Bắt đầu đồng bộ từ tất cả client đã biết");

            var results = new List<SyncResult>();

            // Danh sách IP đã biết - có thể mở rộng để đọc từ cấu hình
            var knownIps = new List<string>
            {
                "192.168.1.100",
                "192.168.1.101",
                "192.168.1.102"
            };

            foreach (var ip in knownIps)
            {
                try
                {
                    var result = await SyncFromIpAsync(ip);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi đồng bộ từ IP {IpAddress}", ip);
                    results.Add(new SyncResult
                    {
                        ClientId = ip,
                        Success = false,
                        Message = "Lỗi: " + ex.Message,
                        Timestamp = DateTime.Now
                    });
                }
            }

            foreach (var result in results)
            {
                AddSyncResult(result);
            }

            return results;
        }

        // Lấy tất cả profile từ hệ thống
        public List<ClientProfile> GetAllProfiles()
        {
            return _profileService.GetAllProfilesAsync().GetAwaiter().GetResult();
        }

        // Lấy profile theo tên
        public ClientProfile GetProfileByName(string profileName)
        {
            var profiles = GetAllProfiles();
            return profiles.FirstOrDefault(p => p.Name == profileName);
        }
    }
}