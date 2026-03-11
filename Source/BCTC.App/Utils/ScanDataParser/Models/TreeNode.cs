using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils.ScanDataParser.Models
{
    public class TreeNode
    {
        public int Index { get; set; }
        public string Text { get; set; }
        public int ParentIndex { get; set; }
        public int Level { get; set; }
        public string Prefix { get; set; }
        public string FullPathText { get; set; }
        public string ParentText { get; set; }
        public int? ReportNormID { get; set; }
    }
}
