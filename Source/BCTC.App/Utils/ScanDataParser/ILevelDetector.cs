using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils.ScanDataParser
{
    public interface ILevelDetector
    {
        int Level { get; }
        bool IsMatch(string text);
        string ExtractPrefix(string text);
    }
}
