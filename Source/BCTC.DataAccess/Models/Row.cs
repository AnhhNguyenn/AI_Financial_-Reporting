namespace BCTC.DataAccess.Models
{
    public class Row
    {
        public string Item { get; set; } = "";
        public Dictionary<string, decimal?> Values { get; set; } = new();
        public string? ReportNormID { get; set; }
        public string? Code { get; set; }
        public string? NormName { get; set; }
        public string? PublishNormCode { get; set; }
        public string? ParentName { get; set; }
    }
}