using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils.ScanDataParser.Models
{
    public class ScanItem
    {
        public int Index { get; set; }
        public string Text { get; set; }
        public string ParentText { get; set; }
        public int? ReportNormID { get; set; }
    }
}
