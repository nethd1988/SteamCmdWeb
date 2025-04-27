using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SteamCmdWeb.Models; // Assuming this namespace is correct from the original .cs file
using SteamCmdWeb.Services; // Assuming this namespace is correct from the original .cs file
// Add potential missing using for Guid if needed
using System.Linq; // Added for FirstOrDefault and Select

namespace SteamCmdWeb.Services // Assuming this namespace is correct from the original .cs file
{
    public class TcpServerService : BackgroundService
    {
        private readonly ILogger<TcpServerService> _logger;
        private readonly ProfileService _profileService; // Assuming ProfileService exists
        private readonly SyncService _syncService; // Assuming SyncService exists
        private readonly ClientTrackingService _clientTrackingService; // Assuming ClientTrackingService exists
        private TcpListener _tcpListener;
        private readonly int _port = 61188; // Port from original .cs file
        private bool _isRunning = false;

        // Constructor from original .cs file
        public TcpServerService(
            ILogger<TcpServerService> logger,
            ProfileService profileService,
            SyncService syncService,
            ClientTrackingService clientTrackingService)
        {
            _logger = logger;
            _profileService = profileService;
            _syncService = syncService;
            _clientTrackingService = clientTrackingService;
        }

        // ExecuteAsync from original .cs file
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _port);
                _tcpListener.Start();
                _isRunning = true;

                _logger.LogInformation("TcpServerService đã khởi động trên cổng {Port}", _port);

                while (!stoppingToken.IsCancellationRequested && _isRunning)
                {
                    try
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync(stoppingToken);
                        // Xử lý client trong một task riêng để không block main thread
                        _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break; // Normal shutdown
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi chấp nhận kết nối TCP");
                        // Optional: Add delay before retrying accept
                        await Task.Delay(1000, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khởi động TcpServerService");
            }
            finally
            {
                _tcpListener?.Stop();
                _isRunning = false;
                _logger.LogInformation("TcpServerService đã dừng");
            }
        }

        // HandleClientAsync from original .cs file
        private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
        {
            string clientIp = "unknown"; // Default value
            try
            {
                // Safely get client IP
                if (client?.Client?.RemoteEndPoint is IPEndPoint remoteEndPoint)
                {
                    clientIp = remoteEndPoint.Address.ToString();
                }
                _logger.LogInformation("Đã nhận kết nối từ {ClientIp}", clientIp);

                using (client) // Ensure client is disposed
                {
                    using NetworkStream stream = client.GetStream();
                    // Set timeouts for read/write operations
                    stream.ReadTimeout = 30000; // 30 seconds
                    stream.WriteTimeout = 30000; // 30 seconds

                    // Đọc dữ liệu từ client
                    byte[] lengthBuffer = new byte[4];
                    // Use ReadAsync with cancellation token properly
                    int bytesRead = await stream.ReadAsync(lengthBuffer.AsMemory(0, 4), stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Không đọc được thông tin độ dài từ client {ClientIp} (bytesRead: {BytesRead})", clientIp, bytesRead);
                        return;
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                    // Add stricter validation for dataLength
                    const int MaxDataLength = 5 * 1024 * 1024; // 5MB limit (adjust as needed)
                    if (dataLength <= 0 || dataLength > MaxDataLength)
                    {
                        _logger.LogWarning("Độ dài dữ liệu không hợp lệ từ client {ClientIp}: {Length}", clientIp, dataLength);
                        // Consider sending an error response before closing
                        await SendResponseAsync(stream, "ERROR:Invalid data length", CancellationToken.None); // Use separate token for final response
                        return;
                    }

                    byte[] dataBuffer = new byte[dataLength];
                    int totalBytesRead = 0;
                    // Loop to ensure all data is read
                    while (totalBytesRead < dataLength && !stoppingToken.IsCancellationRequested)
                    {
                        bytesRead = await stream.ReadAsync(dataBuffer.AsMemory(totalBytesRead, dataLength - totalBytesRead), stoppingToken);
                        if (bytesRead == 0)
                        {
                            // Connection closed prematurely
                            _logger.LogWarning("Kết nối bị đóng sớm bởi client {ClientIp} khi đang đọc dữ liệu.", clientIp);
                            return;
                        }
                        totalBytesRead += bytesRead;
                    }

                    if (totalBytesRead < dataLength)
                    {
                        _logger.LogWarning("Dữ liệu không đầy đủ từ client {ClientIp} (expected: {Expected}, received: {Received})", clientIp, dataLength, totalBytesRead);
                        return;
                    }

                    string command = Encoding.UTF8.GetString(dataBuffer, 0, totalBytesRead);
                    _logger.LogInformation("Nhận lệnh từ client {ClientIp}: {Command}", clientIp, command);

                    // Thêm tracking client
                    string clientId = ExtractClientIdFromCommand(command); // Use the updated method
                    if (!string.IsNullOrEmpty(clientId) && clientId != "unknown") // Avoid tracking unknown clients if possible
                    {
                        _clientTrackingService.TrackClient(clientId, clientIp);
                    }

                    // Xử lý lệnh using the updated method
                    await ProcessCommandAsync(command, stream, clientIp, stoppingToken);
                } // using (client) ensures disposal
            }
            catch (IOException ioEx) when (ioEx.InnerException is SocketException socketEx)
            {
                // Handle specific socket errors like connection reset
                _logger.LogWarning("Lỗi IO/Socket khi xử lý client {ClientIp}: {ErrorCode} - {Message}", clientIp, socketEx.SocketErrorCode, socketEx.Message);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Hoạt động xử lý client {ClientIp} đã bị hủy.", clientIp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi không xác định khi xử lý client {ClientIp}", clientIp);
                // Attempt to send an error response if the stream is still valid
                if (client != null && client.Connected)
                {
                    try
                    {
                        using NetworkStream errorStream = client.GetStream();
                        await SendResponseAsync(errorStream, $"ERROR:Internal server error", CancellationToken.None);
                    }
                    catch (Exception sendEx)
                    {
                        _logger.LogError(sendEx, "Không thể gửi thông báo lỗi cuối cùng tới client {ClientIp}", clientIp);
                    }
                }
            }
            finally
            {
                _logger.LogInformation("Đã đóng kết nối với client {ClientIp}", clientIp);
            }
        }

        // *** UPDATED METHOD from 1.txt ***
        private string ExtractClientIdFromCommand(string command)
        {
            _logger.LogDebug("Extracting ClientID from command: {Command}", command); // [cite: 1]
            // Tìm CLIENT_ID trong command
            if (command.Contains("CLIENT_ID:")) // [cite: 1]
            {
                try
                {
                    int startIndex = command.IndexOf("CLIENT_ID:") + 10; // [cite: 1]
                    int endIndex = command.IndexOf(" ", startIndex); // [cite: 1] Find next space after CLIENT_ID:
                    if (endIndex == -1) endIndex = command.Length; // If no space found, take till end

                    string clientId = command.Substring(startIndex, endIndex - startIndex); // [cite: 1]
                    _logger.LogInformation("Extracted ClientID: {ClientId}", clientId); // [cite: 1]
                    return clientId;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error extracting ClientID from command"); // [cite: 1]
                }
            }

            // Tìm token authentication và dùng làm client ID nếu không có CLIENT_ID
            if (command.StartsWith("AUTH:")) // [cite: 1]
            {
                try
                {
                    // Split carefully: AUTH:token CLIENT_ID:xyz COMMAND or AUTH:token COMMAND
                    string[] parts = command.Split(' ', 3); // Limit splits [cite: 1]
                    if (parts.Length > 0 && parts[0].StartsWith("AUTH:")) // [cite: 1]
                    {
                        string authToken = parts[0].Substring(5); // [cite: 1]
                        // Generate a somewhat stable ID based on token hash for anonymous clients
                        return $"anonymous-{authToken.GetHashCode()}"; // [cite: 1]
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error extracting auth token for fallback ClientID"); // [cite: 1]
                }
            }

            // Tạo ID ngẫu nhiên nếu không tìm thấy
            string randomId = $"unknown-{Guid.NewGuid().ToString().Substring(0, 8)}"; // [cite: 1]
            _logger.LogWarning("Could not extract ClientID or AuthToken, using random ID: {RandomId} for command: {Command}", randomId, command); // [cite: 1]
            return randomId;
        }


        // *** UPDATED METHOD from 1.txt ***
        private async Task ProcessCommandAsync(string command, NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            // Log command để debug
            _logger.LogDebug("Processing command: {Command}", command);

            // Kiểm tra xác thực (đơn giản) - Must start with AUTH:
            if (!command.StartsWith("AUTH:"))
            {
                await SendResponseAsync(stream, "AUTHENTICATION_REQUIRED", stoppingToken);
                _logger.LogWarning("Client {ClientIp} không gửi xác thực (AUTH: prefix missing)", clientIp);
                return;
            }

            // Tìm vị trí của phần lệnh
            string actualCommandPart = "";
            string authToken = "";
            string clientId = null;

            // Tách AUTH:token
            int firstSpace = command.IndexOf(' ');
            if (firstSpace == -1)
            {
                await SendResponseAsync(stream, "INVALID_COMMAND: Missing command part", stoppingToken);
                _logger.LogWarning("Lệnh không hợp lệ từ client {ClientIp} (thiếu phần lệnh): {Command}", clientIp, command);
                return;
            }

            authToken = command.Substring(5, firstSpace - 5); // Get token part
            string remainingCommand = command.Substring(firstSpace + 1);

            // Kiểm tra xem có CLIENT_ID không
            if (remainingCommand.StartsWith("CLIENT_ID:"))
            {
                // Tìm vị trí space tiếp theo sau CLIENT_ID
                int secondSpace = remainingCommand.IndexOf(' ');
                if (secondSpace == -1)
                {
                    await SendResponseAsync(stream, "INVALID_COMMAND: Missing command part after CLIENT_ID", stoppingToken);
                    _logger.LogWarning("Lệnh không hợp lệ từ client {ClientIp} (thiếu lệnh sau CLIENT_ID): {Command}", clientIp, command);
                    return;
                }

                clientId = remainingCommand.Substring(10, secondSpace - 10);
                actualCommandPart = remainingCommand.Substring(secondSpace + 1);
            }
            else
            {
                actualCommandPart = remainingCommand;
            }

            // Kiểm tra token (simple check)
            if (authToken != "simple_auth_token")
            {
                await SendResponseAsync(stream, "INVALID_TOKEN", stoppingToken);
                _logger.LogWarning("Token không hợp lệ từ client {ClientIp}", clientIp);
                return;
            }

            _logger.LogInformation("Client {ClientIp} authenticated successfully. Processing command part: {ActualCommand}", clientIp, actualCommandPart);

            // Xử lý các lệnh cụ thể dựa trên actualCommandPart
            if (actualCommandPart == "GET_PROFILES")
            {
                await HandleGetProfilesAsync(stream, stoppingToken);
            }
            else if (actualCommandPart.StartsWith("GET_PROFILE_DETAILS "))
            {
                string profileName = actualCommandPart.Substring("GET_PROFILE_DETAILS ".Length).Trim();
                await HandleGetProfileDetailsAsync(stream, profileName, stoppingToken);
            }
            else if (actualCommandPart == "SEND_PROFILE")
            {
                await HandleReceiveProfileAsync(stream, clientIp, stoppingToken);
            }
            else if (actualCommandPart == "SEND_PROFILES")
            {
                await HandleReceiveProfilesAsync(stream, clientIp, stoppingToken);
            }
            else
            {
                await SendResponseAsync(stream, "UNKNOWN_COMMAND", stoppingToken);
                _logger.LogWarning("Lệnh không xác định từ client {ClientIp}: {Command}", clientIp, actualCommandPart);
            }
        }


        // HandleGetProfilesAsync from original .cs file
        private async Task HandleGetProfilesAsync(NetworkStream stream, CancellationToken stoppingToken)
        {
            try
            {
                var profiles = await _profileService.GetAllProfilesAsync(); // Assuming this method exists
                if (profiles == null || profiles.Count == 0)
                {
                    await SendResponseAsync(stream, "NO_PROFILES", stoppingToken);
                    return;
                }

                var profileNames = profiles.Select(p => p.Name).ToList();
                string result = string.Join(",", profileNames); // Comma-separated list
                await SendResponseAsync(stream, $"PROFILES:{result}", stoppingToken); // Add prefix for clarity
                _logger.LogInformation("Đã gửi {Count} tên profile", profileNames.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý GET_PROFILES");
                await SendResponseAsync(stream, "ERROR:Failed to get profiles", stoppingToken);
            }
        }

        // HandleGetProfileDetailsAsync from original .cs file
        private async Task HandleGetProfileDetailsAsync(NetworkStream stream, string profileName, CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                await SendResponseAsync(stream, "ERROR:Profile name cannot be empty", stoppingToken);
                _logger.LogWarning("Yêu cầu GET_PROFILE_DETAILS với tên profile rỗng.");
                return;
            }

            try
            {
                // Assuming ProfileService can find by name and decrypt
                // This needs adjustment based on actual ProfileService implementation
                var profiles = await _profileService.GetAllProfilesAsync(); // Get all first
                var profile = profiles?.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)); // Find by name, ignore case

                if (profile == null)
                {
                    await SendResponseAsync(stream, "PROFILE_NOT_FOUND", stoppingToken);
                    _logger.LogWarning("Không tìm thấy profile với tên: {ProfileName}", profileName);
                    return;
                }

                // Assuming a method to get decrypted details by ID exists
                var decryptedProfile = await _profileService.GetDecryptedProfileByIdAsync(profile.Id); // Fetch decrypted version

                if (decryptedProfile == null)
                {
                    // This case might happen if the profile exists but decryption fails or ID is wrong
                    await SendResponseAsync(stream, "ERROR:Failed to retrieve profile details", stoppingToken);
                    _logger.LogError("Không thể lấy thông tin giải mã cho profile ID {ProfileId} (Name: {ProfileName})", profile.Id, profileName);
                    return;
                }


                // Assuming SteamCmdWebAPI.Models.SteamCmdProfile exists and is serializable
                // Map necessary fields from ClientProfile (decrypted) to SteamCmdProfile (for sending)
                var steamCmdProfileToSend = new SteamCmdWebAPI.Models.SteamCmdProfile // Create the API model instance
                {
                    Id = decryptedProfile.Id,
                    Name = decryptedProfile.Name,
                    AppID = decryptedProfile.AppID,
                    InstallDirectory = decryptedProfile.InstallDirectory,
                    Arguments = decryptedProfile.Arguments,
                    ValidateFiles = decryptedProfile.ValidateFiles,
                    AutoRun = decryptedProfile.AutoRun,
                    // Status, StartTime, etc. might not be relevant to send back if client manages state
                    // Only send what the client needs
                    SteamUsername = decryptedProfile.SteamUsername, // Send decrypted username
                    SteamPassword = decryptedProfile.SteamPassword  // Send decrypted password
                };

                string json = JsonSerializer.Serialize(steamCmdProfileToSend);
                await SendResponseAsync(stream, $"PROFILE_DETAILS:{json}", stoppingToken); // Add prefix
                _logger.LogInformation("Đã gửi thông tin chi tiết cho profile {ProfileName}", profileName);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "Lỗi JSON khi xử lý GET_PROFILE_DETAILS cho {ProfileName}", profileName);
                await SendResponseAsync(stream, "ERROR:JSON serialization failed", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý GET_PROFILE_DETAILS cho {ProfileName}", profileName);
                await SendResponseAsync(stream, "ERROR:Failed to get profile details", stoppingToken);
            }
        }

        // HandleReceiveProfileAsync from original .cs file
        private async Task HandleReceiveProfileAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                await SendResponseAsync(stream, "READY_TO_RECEIVE_PROFILE", stoppingToken); // More specific response

                // Đọc độ dài profile
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await stream.ReadAsync(lengthBuffer.AsMemory(0, 4), stoppingToken);
                if (bytesRead < 4)
                {
                    _logger.LogWarning("Không đọc được thông tin độ dài profile từ client {ClientIp}", clientIp);
                    return;
                }

                int dataLength = BitConverter.ToInt32(lengthBuffer, 0);
                const int MaxProfileLength = 5 * 1024 * 1024; // 5MB limit
                if (dataLength <= 0 || dataLength > MaxProfileLength)
                {
                    _logger.LogWarning("Độ dài profile không hợp lệ từ client {ClientIp}: {Length}", clientIp, dataLength);
                    await SendResponseAsync(stream, "ERROR:Invalid profile data length", CancellationToken.None);
                    return;
                }

                byte[] dataBuffer = new byte[dataLength];
                int totalBytesRead = 0;
                while (totalBytesRead < dataLength && !stoppingToken.IsCancellationRequested)
                {
                    bytesRead = await stream.ReadAsync(dataBuffer.AsMemory(totalBytesRead, dataLength - totalBytesRead), stoppingToken);
                    if (bytesRead == 0)
                    {
                        _logger.LogWarning("Kết nối bị đóng sớm bởi client {ClientIp} khi đang nhận profile.", clientIp);
                        return;
                    }
                    totalBytesRead += bytesRead;
                }

                if (totalBytesRead < dataLength)
                {
                    _logger.LogWarning("Dữ liệu profile không đầy đủ từ client {ClientIp}", clientIp);
                    return;
                }


                string json = Encoding.UTF8.GetString(dataBuffer, 0, totalBytesRead);
                _logger.LogDebug("Nhận JSON profile: {Json}", json); // Log received JSON

                // Deserialize using the API model
                var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(json);

                if (steamCmdProfile == null || string.IsNullOrWhiteSpace(steamCmdProfile.Name) || string.IsNullOrWhiteSpace(steamCmdProfile.AppID))
                {
                    _logger.LogWarning("Dữ liệu profile không hợp lệ hoặc thiếu thông tin cần thiết từ client {ClientIp}", clientIp);
                    await SendResponseAsync(stream, "ERROR:Invalid profile data", stoppingToken);
                    return;
                }

                // Validation: Check for required fields if necessary, e.g., login info
                if (string.IsNullOrEmpty(steamCmdProfile.SteamUsername) || string.IsNullOrEmpty(steamCmdProfile.SteamPassword))
                {
                    await SendResponseAsync(stream, "ERROR:Login information (username/password) is required", stoppingToken);
                    _logger.LogWarning("Profile từ client {ClientIp} bị từ chối vì thiếu thông tin đăng nhập: {ProfileName}", clientIp, steamCmdProfile.Name);
                    return;
                }

                _logger.LogInformation("Nhận profile hợp lệ: Name={Name}, AppID={AppID} từ client {ClientIp}",
                    steamCmdProfile.Name, steamCmdProfile.AppID, clientIp);

                // Map from API model to local ClientProfile model
                var clientProfile = new ClientProfile // Assuming ClientProfile is the local model
                {
                    // Map relevant fields
                    Name = steamCmdProfile.Name, // Already checked not null/whitespace
                    AppID = steamCmdProfile.AppID, // Already checked not null/whitespace
                    InstallDirectory = steamCmdProfile.InstallDirectory ?? "", // Use empty string if null
                    Arguments = steamCmdProfile.Arguments ?? "",
                    ValidateFiles = steamCmdProfile.ValidateFiles,
                    AutoRun = steamCmdProfile.AutoRun,
                    // Set default/initial status for pending profiles
                    Status = "Pending Sync",
                    // Store credentials directly (assuming SyncService handles encryption if needed)
                    SteamUsername = steamCmdProfile.SteamUsername,
                    SteamPassword = steamCmdProfile.SteamPassword,
                    // Set timestamps
                    // StartTime = DateTime.Now, // Or set when approved?
                    // StopTime = DateTime.Now,
                    LastRun = DateTime.MinValue // Indicate it hasn't run yet
                };

                // Add to pending list via SyncService
                _syncService.AddPendingProfile(clientProfile); // Assuming this method exists

                await SendResponseAsync(stream, $"SUCCESS:Added profile '{clientProfile.Name}' to pending list", stoppingToken);
                _logger.LogInformation("Đã thêm profile {ProfileName} từ client {ClientIp} vào danh sách chờ.", clientProfile.Name, clientIp);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "Lỗi JSON khi nhận profile từ client {ClientIp}", clientIp);
                await SendResponseAsync(stream, "ERROR:Invalid JSON format for profile", stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi nhận profile từ client {ClientIp}", clientIp);
                // Avoid sending detailed error messages back to client for security
                await SendResponseAsync(stream, $"ERROR:Failed to process received profile", stoppingToken);
            }
        }

        // HandleReceiveProfilesAsync from original .cs file
        private async Task HandleReceiveProfilesAsync(NetworkStream stream, string clientIp, CancellationToken stoppingToken)
        {
            try
            {
                await SendResponseAsync(stream, "READY_TO_RECEIVE_PROFILES", stoppingToken); // More specific

                int processedCount = 0;
                int errorCount = 0;

                // Loop to receive multiple profiles until an end marker (length 0)
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Đọc độ dài profile
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBuffer.AsMemory(0, 4), stoppingToken);
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Không đọc được thông tin độ dài (hoặc marker kết thúc) từ client {ClientIp} khi nhận nhiều profiles", clientIp);
                        break; // Exit loop on read error
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // Check for end marker
                    if (dataLength == 0)
                    {
                        _logger.LogInformation("Nhận được marker kết thúc (length 0) từ client {ClientIp} sau khi nhận {ProcessedCount} profiles", clientIp, processedCount);
                        break; // End of transmission
                    }

                    const int MaxProfileLength = 5 * 1024 * 1024; // 5MB limit per profile
                    if (dataLength < 0 || dataLength > MaxProfileLength) // Check negative length too
                    {
                        _logger.LogWarning("Độ dài profile không hợp lệ từ client {ClientIp} khi nhận nhiều profiles: {Length}", clientIp, dataLength);
                        errorCount++;
                        // We need to decide whether to continue or abort on error
                        // For now, just log, increment error, and continue hoping the next length is valid
                        // Optionally: Send an error response for this specific profile? Difficult in bulk transfer.
                        // Consider reading and discarding the invalid data if possible? Very complex.
                        // Best approach might be to abort the whole transfer on first error. Let's abort.
                        await SendResponseAsync(stream, "ERROR:Invalid data length received during bulk transfer. Aborting.", CancellationToken.None);
                        return; // Abort the whole operation
                    }

                    // Read profile data
                    byte[] dataBuffer = new byte[dataLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < dataLength && !stoppingToken.IsCancellationRequested)
                    {
                        bytesRead = await stream.ReadAsync(dataBuffer.AsMemory(totalBytesRead, dataLength - totalBytesRead), stoppingToken);
                        if (bytesRead == 0)
                        {
                            _logger.LogWarning("Kết nối bị đóng sớm bởi client {ClientIp} khi đang nhận nhiều profiles (profile {Count}).", clientIp, processedCount + 1);
                            await SendResponseAsync(stream, "ERROR:Connection closed prematurely during bulk transfer. Aborting.", CancellationToken.None);
                            return; // Abort
                        }
                        totalBytesRead += bytesRead;
                    }

                    if (totalBytesRead < dataLength)
                    {
                        _logger.LogWarning("Dữ liệu không đầy đủ cho profile {Count} từ client {ClientIp}. Aborting.", processedCount + 1, clientIp);
                        await SendResponseAsync(stream, "ERROR:Incomplete data received during bulk transfer. Aborting.", CancellationToken.None);
                        return; // Abort
                    }


                    // Process the received profile
                    try
                    {
                        string json = Encoding.UTF8.GetString(dataBuffer, 0, totalBytesRead);
                        var steamCmdProfile = JsonSerializer.Deserialize<SteamCmdWebAPI.Models.SteamCmdProfile>(json);

                        if (steamCmdProfile == null || string.IsNullOrWhiteSpace(steamCmdProfile.Name) || string.IsNullOrWhiteSpace(steamCmdProfile.AppID))
                        {
                            _logger.LogWarning("Dữ liệu profile không hợp lệ (profile {Count}) từ client {ClientIp}", processedCount + 1, clientIp);
                            errorCount++;
                            // Decide whether to continue or abort on invalid data
                            continue; // Skip this profile, maybe send individual error?
                        }

                        // Validation (e.g., login info)
                        if (string.IsNullOrEmpty(steamCmdProfile.SteamUsername) || string.IsNullOrEmpty(steamCmdProfile.SteamPassword))
                        {
                            _logger.LogWarning("Profile '{ProfileName}' (profile {Count}) từ client {ClientIp} bị từ chối vì thiếu thông tin đăng nhập.", steamCmdProfile.Name, processedCount + 1, clientIp);
                            errorCount++;
                            continue; // Skip this profile
                        }


                        // Map to local model
                        var clientProfile = new ClientProfile
                        {
                            Name = steamCmdProfile.Name,
                            AppID = steamCmdProfile.AppID,
                            InstallDirectory = steamCmdProfile.InstallDirectory ?? "",
                            Arguments = steamCmdProfile.Arguments ?? "",
                            ValidateFiles = steamCmdProfile.ValidateFiles,
                            AutoRun = steamCmdProfile.AutoRun,
                            Status = "Pending Sync",
                            SteamUsername = steamCmdProfile.SteamUsername,
                            SteamPassword = steamCmdProfile.SteamPassword,
                            LastRun = DateTime.MinValue
                        };

                        // Add to pending list
                        _syncService.AddPendingProfile(clientProfile);

                        processedCount++;
                        _logger.LogInformation("Đã nhận và chờ xử lý profile {Count}: {ProfileName} từ client {ClientIp}", processedCount, clientProfile.Name, clientIp);
                        // Send confirmation for each profile? Might be too chatty. Send summary at the end.

                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Lỗi JSON khi xử lý profile {Count} từ client {ClientIp}", processedCount + 1, clientIp);
                        errorCount++;
                        // Abort on JSON error? Or just skip? Let's skip.
                        continue;
                    }
                    catch (Exception ex) // Catch other processing errors
                    {
                        _logger.LogError(ex, "Lỗi khi xử lý profile {Count} từ client {ClientIp}", processedCount + 1, clientIp);
                        errorCount++;
                        // Skip this profile
                        continue;
                    }
                } // End while loop

                // Send final summary response after receiving end marker (length 0)
                await SendResponseAsync(stream, $"DONE:{processedCount}:{errorCount}", stoppingToken);
                _logger.LogInformation("Hoàn tất nhận profiles từ client {ClientIp}. Thành công: {SuccessCount}, Lỗi/Bỏ qua: {ErrorCount}",
                    clientIp, processedCount, errorCount);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Hoạt động nhận nhiều profiles từ {ClientIp} đã bị hủy.", clientIp);
                // Don't try to send response if cancelled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nghiêm trọng khi nhận nhiều profiles từ client {ClientIp}", clientIp);
                try
                {
                    // Try sending a final error message if possible
                    await SendResponseAsync(stream, $"ERROR:Critical error during bulk profile receive", CancellationToken.None);
                }
                catch { /* Ignore errors sending final error message */ }
            }
        }

        // SendResponseAsync from original .cs file
        private async Task SendResponseAsync(NetworkStream stream, string response, CancellationToken stoppingToken)
        {
            try
            {
                byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                byte[] lengthBytes = BitConverter.GetBytes(responseBytes.Length);

                // Use cancellation token for async operations
                await stream.WriteAsync(lengthBytes.AsMemory(0, 4), stoppingToken);
                await stream.WriteAsync(responseBytes.AsMemory(0, responseBytes.Length), stoppingToken);
                await stream.FlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Log error but don't let it crash the client handling loop if possible
                _logger.LogError(ex, "Không thể gửi phản hồi tới client: {Response}", response);
                // Rethrow if needed, or handle gracefully
                // throw; // Or handle silently
            }
        }

        // StopAsync from original .cs file
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _isRunning = false;
            _tcpListener?.Stop(); // Request listener to stop accepting new connections
            _logger.LogInformation("Yêu cầu dừng TcpServerService...");
            // Allow time for existing connections to potentially finish gracefully?
            // Depends on requirements. The stoppingToken should handle cancellation in HandleClientAsync.
            await base.StopAsync(stoppingToken); // Base class handles waiting for ExecuteAsync to complete
            _logger.LogInformation("TcpServerService đã dừng hoàn toàn.");
        }
    }
}