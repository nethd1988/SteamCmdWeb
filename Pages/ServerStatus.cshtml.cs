using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SteamCmdWeb.Pages
{
    public class ServerLogEntry
    {
        public string Timestamp { get; set; }
        public string Message { get; set; }
        public bool IsError { get; set; }
    }

    public class ConnectionInfo
    {
        public string IPAddress { get; set; }
        public string Timestamp { get; set; }
        public string Action { get; set; }
        public bool Success { get; set; }
    }

    public class ServerStatusModel : PageModel
    {
        private static readonly DateTime _serverStartTime = DateTime.Now;
        private static int _connectionCount = 0;
        private static readonly Random _random = new Random();
        
        public bool IsServerRunning { get; private set; }
        public string Uptime { get; private set; }
        public int ConnectionCount { get; private set; }
        public string ServerIP { get; private set; }
        public int CpuUsage { get; private set; }
        public int MemoryUsage { get; private set; }
        public int DiskUsage { get; private set; }
        public string DataFolder { get; private set; }
        public string ProfilesFolder { get; private set; }
        public List<ServerLogEntry> ServerLogs { get; private set; }
        public List<ConnectionInfo> RecentConnections { get; private set; }

        public void OnGet()
        {
            // TCP Server status
            IsServerRunning = IsPortListening(61188);
            
            // Uptime calculation
            TimeSpan uptime = DateTime.Now - _serverStartTime;
            Uptime = FormatUptime(uptime);
            
            // Generate some random data for demonstration
            ConnectionCount = _connectionCount + _random.Next(1, 5);
            _connectionCount = ConnectionCount;
            
            // Get server IP
            ServerIP = GetLocalIPAddress();
            
            // System resource usage (simulated for demo)
            CpuUsage = _random.Next(5, 30);
            MemoryUsage = _random.Next(30, 60);
            DiskUsage = _random.Next(40, 80);
            
            // Folder paths
            DataFolder = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            ProfilesFolder = Path.Combine(Directory.GetCurrentDirectory(), "Profiles");
            
            // Generate simulated logs
            ServerLogs = GenerateSimulatedLogs();
            
            // Generate simulated connections
            RecentConnections = GenerateSimulatedConnections();
        }
        
        private bool IsPortListening(int port)
        {
            try
            {
                IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpListeners = ipProperties.GetActiveTcpListeners();
                
                return tcpListeners.Any(endpoint => endpoint.Port == port);
            }
            catch
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
        
        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }
        
        private List<ServerLogEntry> GenerateSimulatedLogs()
        {
            var logs = new List<ServerLogEntry>();
            DateTime baseTime = DateTime.Now.AddHours(-1);
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(0).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Server started on port 61188", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(5).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.100 connected", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(5).AddSeconds(10).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.100 authenticated successfully", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(5).AddSeconds(15).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.100 requested profile list", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(5).AddSeconds(30).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.100 requested details for profile 'PUBG: BATTLEGROUNDS'", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(6).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.100 disconnected", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(20).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.105 connection attempt failed: Authentication error", 
                IsError = true 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(25).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.150 connected", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(25).AddSeconds(5).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.150 authenticated successfully", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(25).AddSeconds(10).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.150 sent new profile 'Counter-Strike 2'", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(26).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Client 192.168.1.150 disconnected", 
                IsError = false 
            });
            
            logs.Add(new ServerLogEntry { 
                Timestamp = baseTime.AddMinutes(40).ToString("yyyy-MM-dd HH:mm:ss"), 
                Message = "Profile backup completed successfully", 
                IsError = false 
            });
            
            logs.Reverse(); // Newest first
            return logs;
        }
        
        private List<ConnectionInfo> GenerateSimulatedConnections()
        {
            var connections = new List<ConnectionInfo>();
            DateTime baseTime = DateTime.Now;
            
            connections.Add(new ConnectionInfo {
                IPAddress = "192.168.1.100",
                Timestamp = baseTime.AddHours(-1).AddMinutes(5).ToString("HH:mm:ss"),
                Action = "Authentication",
                Success = true
            });
            
            connections.Add(new ConnectionInfo {
                IPAddress = "192.168.1.100",
                Timestamp = baseTime.AddHours(-1).AddMinutes(5).AddSeconds(15).ToString("HH:mm:ss"),
                Action = "Get Profiles",
                Success = true
            });
            
            connections.Add(new ConnectionInfo {
                IPAddress = "192.168.1.105",
                Timestamp = baseTime.AddHours(-1).AddMinutes(20).ToString("HH:mm:ss"),
                Action = "Authentication",
                Success = false
            });
            
            connections.Add(new ConnectionInfo {
                IPAddress = "192.168.1.150",
                Timestamp = baseTime.AddHours(-1).AddMinutes(25).AddSeconds(5).ToString("HH:mm:ss"),
                Action = "Authentication",
                Success = true
            });
            
            connections.Add(new ConnectionInfo {
                IPAddress = "192.168.1.150",
                Timestamp = baseTime.AddHours(-1).AddMinutes(25).AddSeconds(10).ToString("HH:mm:ss"),
                Action = "Send Profile",
                Success = true
            });
            
            connections.Reverse(); // Newest first
            return connections;
        }
    }
}