using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Report;
using Dapper;
using Microsoft.Data.SqlClient;
using Serilog;
using System.Data;

namespace BCTC.DataAccess
{
    public class InputRepository
    {
        private readonly string _connectionString;

        public InputRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<IEnumerable<CompanyReportDto>> GetInputReportsAsync(int year)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);

                var result = await conn.QueryAsync<CompanyReportDto>(
                    "dta_StatisticalCompanyHaveFileWithoutReport_AI",
                    new
                    {
                        StockCode = "STB",
                        exchangeID = 0,
                        ReportTermID = 3,
                        Year = year,
                        AuditedStatus = 0,
                        CompanyType = -1,
                        Abstract = 0,
                        IsAdjusted = 0,
                        FromCreatedDate = (string?)null,
                        ToCreatedDate = (string?)null
                    },
                    commandType: CommandType.StoredProcedure
                );
                /*var result = await conn.QueryAsync<CompanyReportDto>(
                    "dta_StatisticalCompanyHaveFileWithoutReport_AI",
                    new
                    {
                        StockCode = (string?)null,
                        exchangeID = 0,
                        ReportTermID = -1,
                        Year = year,
                        AuditedStatus = 0,
                        CompanyType = -1,
                        Abstract = 0,
                        IsAdjusted = -1,
                        FromCreatedDate = (string?)null,
                        ToCreatedDate = (string?)null
                    },
                    commandType: CommandType.StoredProcedure
                );*/

                var list = result?.ToList() ?? new List<CompanyReportDto>();
                Log.Information("[DB][INPUT] Đã load {Count} báo cáo từ DB cho năm {Year}", list.Count, year);
                return list;
            }
            catch (SqlException sqlEx)
            {
                Log.Error(sqlEx, "[DB][FAIL] Lỗi SQL khi lấy danh sách báo cáo năm {Year}", year);
                return new List<CompanyReportDto>();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DB][FAIL] Lỗi không xác định khi lấy danh sách báo cáo năm {Year}", year);
                return new List<CompanyReportDto>();
            }
        }

        public async Task<IEnumerable<ReportDataDetailItem>> GetHistoryDataFromStoreAsync(
            string stockCode,
            int year,
            string termCode,
            string unitedCode,
            string adjustedCode,
            string auditedStatusCode)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);

                var result = await conn.QueryAsync<ReportDataDetailItem>(
                    "AI_GetFinancialHistoryData_Strict",
                    new
                    {
                        StockCode = stockCode,
                        Year = year,
                        ReportTermCode = termCode,
                        UnitedCode = unitedCode,
                        AdjustedCode = adjustedCode,
                        AuditedStatusCode = auditedStatusCode
                    },
                    commandType: CommandType.StoredProcedure
                );

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[DB][HISTORY] Fail to get history: {stockCode}-{year}-{termCode}");
                return new List<ReportDataDetailItem>();
            }
        }
    }
}
