namespace BCTC.DataAccess.Models.Norm
{
    public class NormRow
    {
        public string ReportNormID { get; set; } = "";
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? PublishNormCode { get; set; }
        public string? ParentName { get; set; }
    }
}