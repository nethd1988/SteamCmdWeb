using System;
using System.Collections.Generic;

namespace SteamCmdWeb.Models
{
    /// <summary>
    /// Cấu hình đồng bộ hệ thống
    /// </summary>
    public class SyncConfig
    {
        /// <summary>
        /// Bật/tắt tính năng đồng bộ âm thầm
        /// </summary>
        public bool EnableSilentSync { get; set; } = true;
        
        /// <summary>
        /// Chu kỳ đồng bộ tự động (phút)
        /// </summary>
        public int SyncIntervalMinutes { get; set; } = 60;
        
        /// <summary>
        /// Kích thước tối đa cho đồng bộ (byte)
        /// </summary>
        public long MaxSyncSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB
        
        /// <summary>
        /// Bật/tắt tính năng đồng bộ tự động
        /// </summary>
        public bool EnableAutoSync { get; set; } = true;
        
        /// <summary>
        /// Yêu cầu xác thực khi đồng bộ
        /// </summary>
        public bool RequireAuthentication { get; set; } = true;
        
        /// <summary>
        /// Bật/tắt log chi tiết
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = true;
        
        /// <summary>
        /// Thời gian cập nhật cuối
        /// </summary>
        public DateTime LastModified { get; set; } = DateTime.Now;
        
        /// <summary>
        /// Danh sách IP được phép truy cập
        /// </summary>
        public List<string> AllowedIpAddresses { get; set; } = new List<string>();
        
        /// <summary>
        /// Số lượng yêu cầu đồng bộ tối đa trong 1 giờ
        /// </summary>
        public int MaxRequestsPerHour { get; set; } = 60;
        
        /// <summary>
        /// Giới hạn kích thước batch đồng bộ
        /// </summary>
        public int MaxBatchSize { get; set; } = 100;
        
        /// <summary>
        /// Bật/tắt sao lưu tự động trước khi đồng bộ
        /// </summary>
        public bool EnableAutoBackup { get; set; } = true;
        
        /// <summary>
        /// Bật/tắt kiểm tra trùng lặp
        /// </summary>
        public bool EnableDuplicateCheck { get; set; } = true;
    }
}