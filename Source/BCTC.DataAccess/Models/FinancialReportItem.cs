using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Models
{
    public class FinancialReportItem
    {
        public int ScanIndex { get; set; }
        public string Item { get; set; }
        public Dictionary<string, decimal?> Values { get; set; }
        public int? ReportNormID { get; set; }
        public string Code { get; set; }
        public string NormName { get; set; }
        public string PublishNormCode { get; set; }
        public string ParentName { get; set; }
    }
}
