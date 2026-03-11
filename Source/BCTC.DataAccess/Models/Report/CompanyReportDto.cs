using System.Text.Json.Serialization;

namespace BCTC.DataAccess.Models.Report
{
    public class CompanyReportDto
    {
        public string? MaCK { set { StockCode = value; } }
        public string? KyBaoCao { set { ReportTerm = value; } }
        public string? KiemToan { set { AuditedStatus = value; } }

        public string? StockCode { get; set; }
        public string? CompanyName { get; set; }
        public int? BusinessTypeID { get; set; }
        public string? BusinessTypeName { get; set; }
        public int Year { get; set; }
        public string? ReportTerm { get; set; }
        public string? Date { get; set; }
        public string? UnitedName { get; set; }
        public string? AbstractType { get; set; } // Loại báo cáo
        public string? AuditedStatus { get; set; }
        public int? IsAdjusted { get; set; }
        public string? Url { get; set; }
        public int FileInfoID { get; set; }
        public int ProcessingStatus { get; set; }
        public int ProcessingPriority { get; set; }
    }
}
