using BCTC.DataAccess.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BCTC.DataAccess.Repositories.Interfaces
{
    public interface IFinanceFullRepository
    {
        Task<IEnumerable<ReportNorm>> GetAllReportNormsAsync();
        Task<(ReportDataItem, IEnumerable<ReportDataDetailItem>)> GetReportData(string stockCode, int reportTermId, int year, int unitedId, int adjustedId, int auditedStatusId);

        Task<IEnumerable<ReportDataDetailItem>> GetReportDataFull(string stockCode, int reportTermId, int year, int unitedId, string auditedStatusCode, int isQK);
    }
}
