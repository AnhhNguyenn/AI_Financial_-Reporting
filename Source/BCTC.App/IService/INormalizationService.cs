using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Enum;
using MappingReportNorm.Utils.ScanDataParser.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Interfaces.Services
{
    public interface INormalizationService
    {
        Task<List<ReportNorm>> ReportNorms_Database(ReportTemplate reportTemplate, ReportComponentType reportComponentType);
        Task<List<TreeNode>> ReportNorms_Scan(List<ScanItem> list, ReportTemplate reportTemplate, ReportComponentType reportComponentType);
    }
}
