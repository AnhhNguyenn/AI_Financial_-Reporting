using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Report;
using MappingReportNorm.Models;
using MappingReportNorm.Services;
using BCTC.App.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BCTC.App.IService
{
    public interface IFinancialMappingService
    {
        Task<MappingResponse> MapFinancialIndicatorsAsync(
            List<ScannedIndicator> scannedIndicators,
            List<DatabaseIndicator> databaseIndicators,
            int yearPeriod = 0,
            string additionalContext = "",
            string businessContext = "");
        Task MapByNumberCode(FinancialReportModel data);
        Task MapHistoryStrictAsync(FinancialReportModel model, CompanyReportDto reportInput);
    }
    public interface IFinancialReMappingService
    {
        Task<ReMappingResponse> ReMapFinancialIndicatorsAsync(List<FormulaCandidate> candidates, List<ReportNorm> allReportNorms);
    }
}
