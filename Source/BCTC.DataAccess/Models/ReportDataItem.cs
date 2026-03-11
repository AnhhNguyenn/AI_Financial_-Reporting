namespace BCTC.DataAccess.Models
{
    public class ReportDataItem
    {
        public int ReportDataID { get; set; }
        public int CompanyID { get; set; }
        public int ReportTermID { get; set; }
        public int? AuditStatusID { get; set; }
        public int? CurrencyUnitID { get; set; }

        public DateTime? ReportDate { get; set; }

        public int? BasePeriodBegin { get; set; }
        public int? BasePeriodEnd { get; set; }
        public int? PeriodBegin { get; set; }
        public int? PeriodEnd { get; set; }
        public int? YearPeriod { get; set; }

        public string? Comment { get; set; }
        public string? CommentExchange { get; set; }

        public DateTime? CreatedDate { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? EditedDate { get; set; }
        public string? EditedBy { get; set; }
        public DateTime? DeleteDate { get; set; }
        public string? DeletedBy { get; set; }

        public bool IsDelete { get; set; }
        public int? IsAdjusted { get; set; }
        public int? IsUnited { get; set; }
        public int? IsApproved { get; set; }
        public int? IsAbstracted { get; set; }
        public int? IsPublished { get; set; }
        public int? ExportToFile { get; set; }
        public int? IsApproving { get; set; }
        public int? IsCheck { get; set; }

        public int? FinanceInfoID { get; set; }
        public int? ReportQuarter { get; set; }

        public int? KQ { get; set; }
        public int? CD { get; set; }
        public int? LCGT { get; set; }
        public int? CSTC { get; set; }
        public int? LCTT { get; set; }
        public int? HD { get; set; }
        public int? TS { get; set; }
        public int? TSR { get; set; }
        public int? NB { get; set; }

        public DateTime? LastUpdate { get; set; }
        public int? IsTransferred { get; set; }
        public DateTime? InputDate { get; set; }

        public string? CtyKiemToan { get; set; }
        public DateTime? DatePubDepartment { get; set; }

        public decimal? KLCPNY { get; set; }
        public decimal? KLCPLH { get; set; }
        public decimal? KLCPLHDC { get; set; }
        public decimal? KLCPLHBQ { get; set; }
        public decimal? KLCPLHBQDC { get; set; }

        public decimal? ClosePrice { get; set; }
        public decimal? MarketCap { get; set; }
        public decimal? Dividend { get; set; }

        public decimal? SumProfitAfterTax { get; set; }
        public decimal? SumOwnerEquity { get; set; }
        public decimal? SumTotalAssets { get; set; }

        public DateTime? DateAudited { get; set; }
        public string? AuditedNote { get; set; }
        public string? Note { get; set; }

        public decimal? ProfitAfterTax { get; set; }
        public decimal? SumEBIT { get; set; }
        public decimal? SumVonSuDung { get; set; }

        public string? ReportNote { get; set; }
        public string? ReportNoteEn { get; set; }

        public bool IsNoNotesInFile { get; set; }
    }
}
