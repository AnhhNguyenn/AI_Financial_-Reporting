namespace BCTC.DataAccess.Models.Enum
{
    public enum FileProcessingStatusEnum
    {
        Normal = 0,              // File bình thường / chờ xử lý
        Completed = 1,           // Hoàn thành toàn bộ
        Scanned = 2,             // Scan xong
        Mapped = 4,              // Map xong

        DownloadError = -2,      // Lỗi tải file / enqueue
        ScanError = -3,          // Lỗi OCR / Scan
        MapError = -4,           // Lỗi Mapping
        ImportError = -5         // Lỗi Import DB
    }
}
