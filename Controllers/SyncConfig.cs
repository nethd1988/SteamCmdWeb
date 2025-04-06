using System;

namespace SteamCmdWeb.Models
{
    /// <summary>
    /// Phản hồi từ server sau khi đồng bộ dữ liệu
    /// </summary>
    public class SyncResponse
    {
        /// <summary>
        /// Kết quả đồng bộ hóa (thành công hay thất bại)
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// Thông báo từ server
        /// </summary>
        public string Message { get; set; }
        
        /// <summary>
        /// Tổng số profile được xử lý
        /// </summary>
        public int TotalProfiles { get; set; }
        
        /// <summary>
        /// Số profile được thêm mới
        /// </summary>
        public int Added { get; set; }
        
        /// <summary>
        /// Số profile được cập nhật
        /// </summary>
        public int Updated { get; set; }
        
        /// <summary>
        /// Số profile gặp lỗi
        /// </summary>
        public int Errors { get; set; }
        
        /// <summary>
        /// Thời gian hoàn thành đồng bộ
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
    
    /// <summary>
    /// Phản hồi thống kê về đồng bộ (sử dụng cho giao diện)
    /// </summary>
    public class SyncStatusResponse
    {
        /// <summary>
        /// Thời gian đồng bộ cuối cùng
        /// </summary>
        public DateTime LastSyncTime { get; set; }
        
        /// <summary>
        /// Tổng số lần đồng bộ
        /// </summary>
        public int TotalSyncCount { get; set; }
        
        /// <summary>
        /// Số lần đồng bộ thành công
        /// </summary>
        public int SuccessSyncCount { get; set; }
        
        /// <summary>
        /// Số lần đồng bộ thất bại
        /// </summary>
        public int FailedSyncCount { get; set; }
        
        /// <summary>
        /// Số profile được thêm mới trong lần đồng bộ cuối
        /// </summary>
        public int LastSyncAddedCount { get; set; }
        
        /// <summary>
        /// Số profile được cập nhật trong lần đồng bộ cuối
        /// </summary>
        public int LastSyncUpdatedCount { get; set; }
        
        /// <summary>
        /// Số profile gặp lỗi trong lần đồng bộ cuối
        /// </summary>
        public int LastSyncErrorCount { get; set; }
        
        /// <summary>
        /// Trạng thái đồng bộ có được bật không
        /// </summary>
        public bool SyncEnabled { get; set; }
        
        /// <summary>
        /// Thời gian hiện tại của server
        /// </summary>
        public DateTime CurrentTime { get; set; }
    }
}