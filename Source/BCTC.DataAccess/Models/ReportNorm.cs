namespace BCTC.DataAccess.Models
{
    public class ReportNorm
    {
        public int ReportNormID { get; set; }
        public int ReportComponentID { get; set; }
        public string ReportComponentCode { get; set; }
        public int ReportComponentTypeID { get; set; }
        public int ReportTemplateID { get; set; }
        public string Name { get; set; }
        public string NameEn { get; set; }
        public string Name_VST { get; set; }
        public string NameEn_VST { get; set; }
        public string Formula { get; set; }
        public int CalculatedOrdering { get; set; }
        public decimal Ordering { get; set; }
        public int ParentReportNormID { get; set; }
        public string PublishNormCode { get; set; }
        public int CssStyleID { get; set; }
        public int PaddingStyleID { get; set; }
        public string FullPathName { get; set; }
        public string ParentName { get; set; }
    }
}
