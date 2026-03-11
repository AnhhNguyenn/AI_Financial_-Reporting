namespace BCTC.DataAccess.Models
{
    public class ReportDataDetailItem
    {
        public int ReportNormID { get; set; }
        public int? IsCumulative { get; set; }
        public string? Code { get; set; }
        public decimal? Value { get; set; }
    }
}
