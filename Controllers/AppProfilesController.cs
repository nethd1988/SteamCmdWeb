using Microsoft.AspNetCore.Mvc;
using SteamCmdWeb.Models;
using SteamCmdWeb.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SteamCmdWeb.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AppProfilesController : ControllerBase
    {
        private readonly string _dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
        private readonly AppProfileManager _profileManager;

        public AppProfilesController(AppProfileManager profileManager)
        {
            _profileManager = profileManager;
        }

        [HttpGet]
        public IActionResult GetAppProfiles()
        {
            var profiles = _profileManager.GetAllProfiles();
            return Ok(profiles);
        }

        [HttpGet("{id:int}")]
        public IActionResult GetAppProfileById(int id)
        {
            var profile = _profileManager.GetProfileById(id);
            if (profile == null)
                return NotFound("App Profile not found");

            return Ok(profile);
        }

        [HttpGet("name/{profileName}")]
        public IActionResult GetAppProfileByName(string profileName)
        {
            var profile = _profileManager.GetProfileByName(profileName);
            if (profile == null)
                return NotFound("App Profile not found");

            return Ok(profile);
        }

        [HttpPost]
        public IActionResult CreateAppProfile([FromBody] ClientProfile profile)
        {
            if (profile == null)
                return BadRequest("Invalid profile data");

            // Mã hóa thông tin đăng nhập nếu được cung cấp
            if (!string.IsNullOrEmpty(profile.SteamUsername) && !profile.SteamUsername.StartsWith("w5"))
            {
                profile.SteamUsername = _profileManager.EncryptString(profile.SteamUsername);
            }

            if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.SteamPassword.StartsWith("HEQ"))
            {
                profile.SteamPassword = _profileManager.EncryptString(profile.SteamPassword);
            }

            var result = _profileManager.AddProfile(profile);
            return CreatedAtAction(nameof(GetAppProfileById), new { id = result.Id }, result);
        }

        [HttpPut("{id:int}")]
        public IActionResult UpdateAppProfile(int id, [FromBody] ClientProfile profile)
        {
            if (profile == null || id != profile.Id)
                return BadRequest("Invalid profile data");

            var existingProfile = _profileManager.GetProfileById(id);
            if (existingProfile == null)
                return NotFound("App Profile not found");

            // Mã hóa thông tin đăng nhập nếu được cập nhật
            if (!string.IsNullOrEmpty(profile.SteamUsername) && !profile.SteamUsername.StartsWith("w5"))
            {
                profile.SteamUsername = _profileManager.EncryptString(profile.SteamUsername);
            }

            if (!string.IsNullOrEmpty(profile.SteamPassword) && !profile.SteamPassword.StartsWith("HEQ"))
            {
                profile.SteamPassword = _profileManager.EncryptString(profile.SteamPassword);
            }

            bool updated = _profileManager.UpdateProfile(profile);
            if (!updated)
                return NotFound("Failed to update App Profile");

            return Ok(profile);
        }

        [HttpDelete("{id:int}")]
        public IActionResult DeleteAppProfile(int id)
        {
            bool deleted = _profileManager.DeleteProfile(id);
            if (!deleted)
                return NotFound("App Profile not found");

            return NoContent();
        }

        [HttpGet("{id:int}/decrypt")]
        public IActionResult GetDecryptedCredentials(int id)
        {
            var profile = _profileManager.GetProfileById(id);
            if (profile == null)
                return NotFound("App Profile not found");

            // Chỉ cho phép truy cập từ localhost
            if (!Request.Host.Host.Equals("localhost") && !Request.Host.Host.Equals("127.0.0.1"))
                return Forbid("This operation is only allowed from localhost");

            var credentials = new
            {
                Username = _profileManager.DecryptString(profile.SteamUsername),
                Password = _profileManager.DecryptString(profile.SteamPassword)
            };

            return Ok(credentials);
        }
        
        [HttpGet("sync")]
        public async Task<IActionResult> SyncProfiles(string targetServer = null, int port = 61188)
        {
            try
            {
                if (string.IsNullOrEmpty(targetServer))
                {
                    return BadRequest("Server address is required");
                }

                var profiles = _profileManager.GetAllProfiles();
                int successCount = 0;
                List<string> failedProfiles = new List<string>();
                
                // Giả lập TCP client để gửi dữ liệu
                using (var client = new TcpClient())
                {
                    try
                    {
                        // Set timeout để tránh chờ đợi quá lâu
                        var connectTimeout = TimeSpan.FromSeconds(5);
                        var task = client.ConnectAsync(targetServer, port);
                        if (await Task.WhenAny(task, Task.Delay(connectTimeout)) != task)
                        {
                            return StatusCode(500, "Connection to server timed out");
                        }
                        
                        using (var stream = client.GetStream())
                        {
                            // Gửi thông báo đến server
                            string authCommand = $"AUTH:simple_auth_token SEND_PROFILES";
                            byte[] authBytes = Encoding.UTF8.GetBytes(authCommand);
                            byte[] authLengthBytes = BitConverter.GetBytes(authBytes.Length);
                            
                            await stream.WriteAsync(authLengthBytes, 0, authLengthBytes.Length);
                            await stream.WriteAsync(authBytes, 0, authBytes.Length);
                            
                            // Đọc phản hồi
                            byte[] responseBuffer = new byte[1024];
                            byte[] lengthBuffer = new byte[4];
                            
                            await stream.ReadAsync(lengthBuffer, 0, 4);
                            int responseLength = BitConverter.ToInt32(lengthBuffer, 0);
                            
                            if (responseLength > responseBuffer.Length || responseLength <= 0)
                            {
                                return BadRequest("Invalid response length received from server");
                            }
                            
                            int bytesRead = await stream.ReadAsync(responseBuffer, 0, responseLength);
                            string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                            
                            if (response != "READY_TO_RECEIVE")
                            {
                                return BadRequest($"Server response: {response}");
                            }
                            
                            // Gửi từng profile
                            foreach (var profile in profiles)
                            {
                                try
                                {
                                    string profileJson = JsonSerializer.Serialize(profile);
                                    byte[] profileBytes = Encoding.UTF8.GetBytes(profileJson);
                                    byte[] lengthBytes = BitConverter.GetBytes(profileBytes.Length);
                                    
                                    await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length);
                                    await stream.WriteAsync(profileBytes, 0, profileBytes.Length);
                                    
                                    successCount++;
                                }
                                catch (Exception ex)
                                {
                                    failedProfiles.Add(profile.Name);
                                }
                                
                                // Thêm một chút độ trễ giữa các profile để tránh quá tải server
                                await Task.Delay(100);
                            }
                            
                            // Gửi marker kết thúc
                            byte[] endMarker = BitConverter.GetBytes(0);
                            await stream.WriteAsync(endMarker, 0, endMarker.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, $"Error connecting to server: {ex.Message}");
                    }
                }
                
                return Ok(new { 
                    Success = true, 
                    Message = $"Synced {successCount} profiles successfully", 
                    FailedProfiles = failedProfiles 
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }
    }
}