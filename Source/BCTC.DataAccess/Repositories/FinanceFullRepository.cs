using BCTC.DataAccess.Models;
using BCTC.DataAccess.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Repositories
{
    public class FinanceFullRepository : BaseRepository, IFinanceFullRepository
    {
        private readonly IConfiguration _configuration;
        protected override string ConnectionString => _configuration.GetConnectionString("FinanceFull");

        protected override int DefaultCommandTimeout => 60;

        public FinanceFullRepository(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<IEnumerable<ReportNorm>> GetAllReportNormsAsync()
        {
            return await ExecuteStoredProcAsync<ReportNorm>(
                storedProcName: "sp_ReportNorm_GetListAll"
            );
        }

        public async Task<(ReportDataItem, IEnumerable<ReportDataDetailItem>)> GetReportData(string stockCode, int reportTermId, int year, int unitedId, int adjustedId, int auditedStatusId)
        {
            var parameters = new
            {
                StockCode = stockCode,
                ReportTermID = reportTermId,
                Year = year,
                UnitedID = unitedId,
                AdjustedID = adjustedId,
                AuditedStatusID = auditedStatusId
            };

            using var grid = await ExecuteStoredProcMultipleAsync(
                storedProcName: "AI_Financial_GetReportData_Live2024",
                parameters: parameters
            );

            var reportData = await grid.ReadFirstOrDefaultAsync<ReportDataItem>();

            var reportDetails = await grid.ReadAsync<ReportDataDetailItem>();

            return (reportData, reportDetails);
        }
        private static string MapUnitedCode(int unitedId) => unitedId switch
        {
            0 => "Hopnhat",
            1 => "Donle",
            2 => "Congtyme",
            _ => "Donle"
        };
        private static string MapTermCode(int termId) => termId switch
        {
            1 => "N",
            2 => "Q1",
            3 => "Q2",
            4 => "Q3",
            5 => "Q4",
            9 => "6D",
            12 => "9D",
            _ => throw new ArgumentOutOfRangeException(nameof(termId))
        };

        public async Task<IEnumerable<ReportDataDetailItem>> GetReportDataFull(string stockCode, int reportTermId, int year, int unitedId, string auditedStatusCode, int isQK)
        {
            var parameters = new
            {
                StockCode = stockCode,
                TermCode = MapTermCode(reportTermId),
                YearPeriod = year,
                UnitCode = MapUnitedCode(unitedId),
                Kiemduyet = auditedStatusCode,
                isQK = isQK
            };

            using var grid = await ExecuteStoredProcMultipleAsync(
                "dta_GetReportDataAndDetails",
                parameters
            );

            await grid.ReadAsync<object>();

            var details = (await grid.ReadAsync<ReportDataDetailItem>()).ToList();
            return details;
        }

    }
}
