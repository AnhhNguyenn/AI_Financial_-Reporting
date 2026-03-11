using BCTC.DataAccess.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Interfaces.Services
{
    public interface IFinancialService
    {
        Task<IEnumerable<ReportNorm>> GetAllReportNormsAsync();
        Task<IEnumerable<ReportNorm>> GetAllReportNormsDB();
        Task<(ReportDataItem, IEnumerable<ReportDataDetailItem>)> GetReportData(string stockCode, string reportTermCode, int year, string unitedCode, string adjustedCode, string auditedStatusCode);
        Task<IEnumerable<ReportDataDetailItem>> GetReportDataFull(string stockCode, string reportTermCode, int year, string unitedCode, string auditedStatusCode, int isQK);
    }
}
