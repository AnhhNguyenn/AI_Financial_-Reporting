using BCTC.DataAccess;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Report;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Serilog;
using System.Data;

namespace BCTC.App.Services.InputServices
{
    public class InputService
    {
        private readonly InputRepository _repo;
        private readonly string _connectionString;
        private readonly BctcOptions _opt;

        public InputService(InputRepository repo, IOptions<BctcOptions> opt)
        {
            _repo = repo;
            _connectionString = opt.Value.ConnectionString;
            _opt = opt.Value;
        }

        public async Task<List<CompanyReportDto>> GetPendingReportsAsync(int year)
        {
            try
            {
                Log.Information("[DB][GetPendingReportsAsync][START] Year={Year}", year);
                var reports = await _repo.GetInputReportsAsync(year);
                var list = reports?.ToList() ?? new List<CompanyReportDto>();
                if (list.Count == 0)
                {
                    Log.Information("[DB][GetPendingReportsAsync][DONE] No reports found.");
                    return list;
                }
                var configDict = _opt.ProcessingPriority;
                if (configDict == null || configDict.Count == 0)
                {
                    configDict = new Dictionary<string, int> { { "N", 1 }, { "Q1", 2 }, { "Q2", 3 }, { "Q3", 4 }, { "Q4", 5 }, { "6D", 9 }, { "9D", 12 } };
                    Log.Warning("[PriorityFilter] Config 'ProcessingPriority' is missing. Using default hardcoded values.");
                }
                var priorityMap = new Dictionary<string, int>(configDict, StringComparer.OrdinalIgnoreCase);
                int GetScore(string? term)
                {
                    if (string.IsNullOrEmpty(term)) return 999;
                    var key = term.Trim().ToUpper();
                    return priorityMap.ContainsKey(key) ? priorityMap[key] : 100;
                }
                int minScoreFound = list.Min(x => GetScore(x.ReportTerm));
                var exclusiveList = list
                    .Where(x => GetScore(x.ReportTerm) == minScoreFound)
                    .OrderBy(x => x.FileInfoID)
                    .ToList();
                if (exclusiveList.Count > 0)
                {
                    var typeName = exclusiveList[0].ReportTerm;
                    Log.Information("[PriorityFilter] Batch contains types: {Types}", string.Join(", ", list.Select(x => x.ReportTerm).Distinct()));
                    Log.Information("[PriorityFilter] => EXCLUSIVE MODE: Selected {Count} files of type '{Type}' (Priority {Score}). Others ignored.",
                        exclusiveList.Count, typeName, minScoreFound);
                }
                Log.Information("[DB][GetPendingReportsAsync][DONE] Final Loaded={Count}", exclusiveList.Count);
                return exclusiveList;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DB][GetPendingReportsAsync][FAIL] Year={Year}", year);
                return new List<CompanyReportDto>();
            }
        }

        public async Task<List<CompanyReportDto>> GetRetryReportsAsync(int year)
        {
            try
            {
                Log.Information("[DB][GetRetryReportsAsync][START] Year={Year}", year);

                var reports = await _repo.GetInputReportsAsync(year);
                if (reports == null || !reports.Any())
                {
                    Log.Information("[DB][GetRetryReportsAsync][DONE] No reports");
                    return new List<CompanyReportDto>();
                }

                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"
                    SELECT FileInfoID, ProcessingStatus
                    FROM FileProcessingStatus
                    WHERE ProcessingStatus IN (-2, -3, -4, -5)";

                var failed = await conn.QueryAsync<(int FileInfoID, int ProcessingStatus)>(sql);
                var failedMap = failed.ToDictionary(x => x.FileInfoID, x => x.ProcessingStatus);

                var retry = reports
                    .Where(r => failedMap.ContainsKey(r.FileInfoID))
                    .ToList();

                Log.Information("[DB][GetRetryReportsAsync][DONE] Count={Count}", retry.Count);
                return retry;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DB][GetRetryReportsAsync][FAIL] Year={Year}", year);
                return new List<CompanyReportDto>();
            }
        }

        public async Task<Dictionary<int, int>> GetProcessingStatusMapAsync(CancellationToken ct)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                const string sql = @"
                    SELECT FileInfoID, ProcessingStatus
                    FROM FileProcessingStatus";

                var list = await conn.QueryAsync<(int FileInfoID, int ProcessingStatus)>(sql);
                return list.ToDictionary(x => x.FileInfoID, x => x.ProcessingStatus);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DB][GetProcessingStatusMapAsync][FAIL]");
                return new Dictionary<int, int>();
            }
        }

        public async Task<int> CountRetryAsync(CancellationToken ct)
        {
            return await CountStatusInAsync(new[] { 0, -2, -3, -4, -5 }, ct);
        }

        public async Task UpdateProcessingStatusAsync(int fileInfoId, int status, CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);

            await conn.ExecuteAsync(
                "AI_UpsertFileProcessingStatus",
                new
                {
                    FileInfoID = fileInfoId,
                    Status = status
                },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task<int> CountStatusInAsync(int[] statuses, CancellationToken ct)
        {
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                string sql = $"SELECT COUNT(*) FROM FileProcessingStatus WHERE ProcessingStatus IN ({string.Join(",", statuses)})";

                var count = Convert.ToInt32(await new SqlCommand(sql, conn).ExecuteScalarAsync(ct));

                Log.Information("[DB][CountStatusInAsync][DONE] Statuses={Statuses} Count={Count}",
                    string.Join(",", statuses), count);

                return count;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DB][CountStatusInAsync][FAIL] Statuses={Statuses}",
                    string.Join(",", statuses));
                return 0;
            }
        }

        public async Task<IEnumerable<ReportDataDetailItem>> GetHistoryDataAsync(
            string stockCode,
            int year,
            string term,
            string unitedCode,
            string adjustedCode,
            string auditStatusCode)
        {
            return await _repo.GetHistoryDataFromStoreAsync(
                stockCode,
                year,
                term,
                unitedCode,
                adjustedCode,
                auditStatusCode
            );
        }
    }
}
