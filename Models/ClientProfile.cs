using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SteamCmdWeb.Models
{
    /// <summary>
    /// Đại diện cho một profile game trong hệ thống
    /// </summary>
    public class ClientProfile
    {
        /// <summary>
        /// ID duy nhất của profile
        /// </summary>
        public int Id { get; set; }
        
        /// <summary>
        /// Tên của game hoặc profile
        /// </summary>
        [Required(ErrorMessage = "Tên game là bắt buộc")]
        public string Name { get; set; }
        
        /// <summary>
        /// Steam App ID của game
        /// </summary>
        [Required(ErrorMessage = "Steam App ID là bắt buộc")]
        public string AppID { get; set; }
        
        /// <summary>
        /// Đường dẫn đến thư mục cài đặt game
        /// </summary>
        [Required(ErrorMessage = "Đường dẫn cài đặt là bắt buộc")]
        public string InstallDirectory { get; set; }
        
        /// <summary>
        /// Tên đăng nhập Steam đã được mã hóa
        /// </summary>
        public string SteamUsername { get; set; }
        
        /// <summary>
        /// Mật khẩu Steam đã được mã hóa
        /// </summary>
        public string SteamPassword { get; set; }
        
        /// <summary>
        /// Các tham số bổ sung khi khởi chạy game
        /// </summary>
        public string Arguments { get; set; }
        
        /// <summary>
        /// Có xác thực tính toàn vẹn file khi cài đặt không
        /// </summary>
        public bool ValidateFiles { get; set; }
        
        /// <summary>
        /// Có tự động chạy khi khởi động không
        /// </summary>
        public bool AutoRun { get; set; }
        
        /// <summary>
        /// Sử dụng đăng nhập ẩn danh
        /// </summary>
        public bool AnonymousLogin { get; set; }
        
        /// <summary>
        /// Trạng thái hiện tại của game
        /// </summary>
        public string Status { get; set; }
        
        /// <summary>
        /// Thời điểm bắt đầu chạy
        /// </summary>
        public DateTime StartTime { get; set; }
        
        /// <summary>
        /// Thời điểm dừng
        /// </summary>
        public DateTime StopTime { get; set; }
        
        /// <summary>
        /// Process ID nếu đang chạy
        /// </summary>
        public int Pid { get; set; }
        
        /// <summary>
        /// Thời điểm chạy gần nhất
        /// </summary>
        public DateTime LastRun { get; set; }
        
        /// <summary>
        /// Kích thước cài đặt (tùy chọn)
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? InstallSize { get; set; }
        
        /// <summary>
        /// Số phiên đã chạy (tùy chọn)
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SessionCount { get; set; }
        
        /// <summary>
        /// Ghi chú về game (tùy chọn)
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Notes { get; set; }
        
        /// <summary>
        /// Khởi tạo một đối tượng profile mới với các giá trị mặc định
        /// </summary>
        public ClientProfile()
        {
            // Thiết lập giá trị mặc định
            Id = 0;
            Name = string.Empty;
            AppID = string.Empty;
            InstallDirectory = string.Empty;
            Arguments = string.Empty;
            Status = "Ready";
            StartTime = DateTime.Now;
            StopTime = DateTime.Now;
            LastRun = DateTime.UtcNow;
            ValidateFiles = false;
            AutoRun = false;
            AnonymousLogin = false;
            Pid = 0;
        }
        
        /// <summary>
        /// Kiểm tra xem profile có hợp lệ không
        /// </summary>
        /// <returns>True nếu hợp lệ, False nếu không</returns>
        public bool IsValid()
        {
            if (string.IsNullOrEmpty(Name)) return false;
            if (string.IsNullOrEmpty(AppID)) return false;
            if (string.IsNullOrEmpty(InstallDirectory)) return false;
            
            if (!AnonymousLogin && (string.IsNullOrEmpty(SteamUsername) || string.IsNullOrEmpty(SteamPassword)))
            {
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Tạo bản sao của profile hiện tại
        /// </summary>
        /// <returns>Một bản sao của profile</returns>
        public ClientProfile Clone()
        {
            return new ClientProfile
            {
                Id = 0, // ID mới
                Name = $"{Name} (Copy)",
                AppID = AppID,
                InstallDirectory = InstallDirectory,
                SteamUsername = SteamUsername,
                SteamPassword = SteamPassword,
                Arguments = Arguments,
                ValidateFiles = ValidateFiles,
                AutoRun = AutoRun,
                AnonymousLogin = AnonymousLogin,
                Status = "Ready",
                StartTime = DateTime.Now,
                StopTime = DateTime.Now,
                LastRun = DateTime.UtcNow,
                Pid = 0,
                InstallSize = InstallSize,
                SessionCount = 0,
                Notes = Notes
            };
        }
    }
}