using BCTC.App.IService;
using BCTC.App.Services.GeminiServices;
using BCTC.App.Services.InputServices;
using BCTC.App.Services.ScanFix;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Report;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;

namespace BCTC.App.Services.BctcServices
{
    public class Bctcservice : IBctcservice
    {
        private readonly GeminiService _gemini;
        private readonly BctcOptions _opt;
        private readonly InputService _inputService;
        private readonly DocumentCleaner _documentCleaner;

        public Bctcservice(
            GeminiService gemini,
            IOptions<BctcOptions> opt,
            InputService inputService,
            DocumentCleaner documentCleaner)
        {
            _gemini = gemini;
            _opt = opt.Value;
            _inputService = inputService;
            _documentCleaner = documentCleaner;
        }

        public async Task<(ExtractResult data, string modelUsed, UsageInfo usage)> ExtractAsync(
            string name, byte[] pdfBytes, string mime, string? normPath,
            string? modelOverride, bool refineWithGemini, int? businessTypeId, CancellationToken ct)

        {
            const string tag = "[BctcService.ExtractAsync]";
            string fileName = Path.GetFileNameWithoutExtension(name);
            string[] parts = fileName.Split('_', StringSplitOptions.RemoveEmptyEntries);
            string company = parts.Length > 0 ? parts[0].Trim().ToUpper() : "";
            string reportTerm = parts.FirstOrDefault(p =>
                p.StartsWith("Q", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("6T", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("9T", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("N", StringComparison.OrdinalIgnoreCase)
            )?.Trim().ToUpper() ?? "Q1";
            int year = parts.Select(p => int.TryParse(p, out int y) ? y : 0).FirstOrDefault(y => y >= 2000 && y <= 3000);
            string key = Path.GetFileName(name);

            Log.Information($"{tag} START {key}");
            var swScan = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var reportDto = new CompanyReportDto
                {
                    StockCode = company,
                    CompanyName = company,
                    Year = year,
                    ReportTerm = reportTerm,
                    Url = name,
                    BusinessTypeID = businessTypeId
                };

                var (extractResult, modelUsed, usage) = await _gemini.ScanAsync(
                    reportDto,
                    pdfBytes,
                    mime,
                    modelOverride,
                    ct
                );

                if (extractResult == null) throw new InvalidOperationException("Gemini OCR không trả dữ liệu hợp lệ");

                extractResult.Meta ??= new Meta { MaCongTy = company, KyBaoCao = reportTerm, Nam = year };
                if (string.IsNullOrWhiteSpace(extractResult.Currency)) extractResult.Currency = "VND";

                /*if (refineWithGemini && !string.IsNullOrEmpty(normPath))
                {
                    try
                    {
                        var refineUsage = await _gemini.RefineNormAsync(extractResult, normPath, ct);
                        if (refineUsage != null) { usage.MapInputTokens += refineUsage.MapInputTokens; usage.MapOutputTokens += refineUsage.MapOutputTokens; }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"{tag} REFINE FAIL {key}");
                    }
                }*/

                swScan.Stop();
                var sec = swScan.Elapsed.TotalSeconds;

                Log.Information("[METRICS][SCAN-TIME] File={File} Time={Sec:F1}s", key, sec);
                Log.Information("[METRICS][SCAN-TOKEN] File={File} In={In} Out={Out} Total={Total}",
                    key, usage.ScanInputTokens, usage.ScanOutputTokens, usage.ScanTotalTokens);

                return (extractResult, modelUsed, usage);
            }
            catch (Exception ex) { Log.Error(ex, $"{tag} FATAL {key}"); throw; }
        }

        public async Task<UsageInfo> MapAsync(ExtractResult result, string normFolder, CancellationToken ct)
        {
            try
            {
                var swMap = System.Diagnostics.Stopwatch.StartNew();

                var usage = await _gemini.RefineNormAsync(result, normFolder, ct) ?? UsageInfo.Empty;

                DeduplicateMappedData(result);

                swMap.Stop();
                string fileKey = result?.Company ?? result?.Meta?.MaCongTy ?? "UNKNOWN";
                var sec = swMap.Elapsed.TotalSeconds;
                Log.Information("[METRICS][MAP-TIME] File={File} Time={Sec:F1}s", fileKey, sec);
                Log.Information("[METRICS][MAP-TOKEN] File={File} In={In} Out={Out} Total={Total}",
                    fileKey, usage.MapInputTokens, usage.MapOutputTokens, usage.MapTotalTokens);
                return usage;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BctcService.MapAsync] FAIL");
                throw;
            }
        }

        private void DeduplicateMappedData(ExtractResult result)
        {
            var allRows = new List<Row>();
            if (result.IncomeStatement != null) allRows.AddRange(result.IncomeStatement);
            if (result.BalanceSheet != null) allRows.AddRange(result.BalanceSheet);
            if (result.CashFlow != null) allRows.AddRange(result.CashFlow);

            var groups = allRows
                .Where(r => !string.IsNullOrEmpty(r.ReportNormID))
                .GroupBy(r => r.ReportNormID)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var winner = group
                    .OrderByDescending(r => r.Values != null && r.Values.Count > 0)
                    .ThenByDescending(r => !string.IsNullOrEmpty(r.Code))
                    .First();

                foreach (var loser in group)
                {
                    if (loser == winner) continue;
                    loser.ReportNormID = null;
                }
            }
        }

        public List<ExtractResult> SplitCashFlowResult(ExtractResult original)
        {
            var results = new List<ExtractResult>();
            string tag = "[SPLIT-LOGIC]";

            if (original.Meta == null)
            {
                results.Add(original);
                return results;
            }

            string maCongTy = original.Meta.MaCongTy ?? "UNK";
            string kyBaoCao = (original.Meta.KyBaoCao ?? "").Trim().ToUpperInvariant();

            var splitTargetTerms = new HashSet<string> { "Q2", "Q3", "Q4" };

            if (!splitTargetTerms.Contains(kyBaoCao))
            {
                Log.Information($"{tag} {maCongTy}-{kyBaoCao}: Ky mac dinh -> GIU NGUYEN.");
                results.Add(original);
                return results;
            }

            bool isLuyKe = IsCashFlowAccumulated(original);

            if (!isLuyKe)
            {
                Log.Information($"{tag} {maCongTy}-{kyBaoCao}: LCTT la Quy -> KHONG TACH.");
                results.Add(original);
                return results;
            }

            Log.Information($"{tag} {maCongTy}-{kyBaoCao}: LCTT la Luy Ke (hoac Ngan Hang) -> TACH FILE.");

            string newTerm = kyBaoCao switch
            {
                "Q2" => "6D",
                "Q3" => "9D",
                "Q4" => "N",
                _ => kyBaoCao
            };

            try
            {
                string jsonClone = JsonSerializer.Serialize(original);
                var banLuyKe = JsonSerializer.Deserialize<ExtractResult>(jsonClone);

                if (banLuyKe != null)
                {
                    banLuyKe.Meta.KyBaoCao = newTerm;
                    CleanDataByPeriod(banLuyKe, keepAccumulated: true);
                    results.Add(banLuyKe);
                    Log.Information($"{tag} -> Tao ban: {newTerm}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{tag} Loi tao ban luy ke");
            }

            CleanDataByPeriod(original, keepAccumulated: false);

            if (original.CashFlow != null)
            {
                bool hasValue = original.CashFlow.Any(x => x.Values != null && x.Values.Count > 0);
                if (hasValue)
                {
                    original.CashFlow.Clear();
                    original.CashFlowMethod = null;
                }
            }

            results.Add(original);
            Log.Information($"{tag} -> Cap nhat ban goc: {kyBaoCao}");

            return results;
        }

        private void CleanDataByPeriod(ExtractResult data, bool keepAccumulated)
        {
            void FilterRows(List<Row>? rows)
            {
                if (rows == null) return;
                foreach (var row in rows)
                {
                    if (row.Values == null || row.Values.Count == 0) continue;

                    var keysToRemove = row.Values.Keys.Where(k =>
                    {
                        string keyUpper = k.ToUpperInvariant();
                        bool isAccumulatedKey = keyUpper.Contains("6T") || keyUpper.Contains("9T") ||
                                                keyUpper.Contains("N") || keyUpper.Contains("NAM") ||
                                                keyUpper.Contains("6D") || keyUpper.Contains("9D") ||
                                                keyUpper.Contains("LUY KE") || keyUpper.Contains("ACCUMULATED") ||
                                                keyUpper.Contains("DAU NAM");

                        if (keepAccumulated)
                            return !isAccumulatedKey;
                        else
                            return isAccumulatedKey;
                    }).ToList();

                    foreach (var k in keysToRemove)
                    {
                        row.Values.Remove(k);
                    }
                }
            }

            FilterRows(data.IncomeStatement);
            FilterRows(data.BalanceSheet);
            FilterRows(data.CashFlow);
        }

        private bool IsCashFlowAccumulated(ExtractResult data)
        {
            string tag = "[CHECK-LCTT]";
            string idInfo = $"{data.Meta?.MaCongTy}-{data.Meta?.KyBaoCao}";

            try
            {
                int bizType = data.BusinessTypeID ?? 1;

                if (bizType == 3)
                {
                    Log.Information($"{tag} {idInfo} | Ngan hang (ID=3) -> Mac dinh LUY KE.");
                    return true;
                }

                bool isSecurities = (bizType == 2);

                string[] codesProfitIS = isSecurities ? new[] { "09", "9" } : new[] { "50", "60" };
                string[] codesProfitCF = new[] { "01" };

                string[] codesCashBS = new[] { "110" };
                string[] codesCashCF = isSecurities ? new[] { "101" } : new[] { "60", "61" };

                var rowProfitIS = data.IncomeStatement?.FirstOrDefault(r => codesProfitIS.Contains(r.Code));
                var rowProfitCF = data.CashFlow?.FirstOrDefault(r => codesProfitCF.Contains(r.Code));

                var rowCashBS = data.BalanceSheet?.FirstOrDefault(r => codesCashBS.Contains(r.Code));
                var rowCashCF = data.CashFlow?.FirstOrDefault(r => codesCashCF.Contains(r.Code));

                decimal valProfitIS = GetCurrentValue(rowProfitIS?.Values);
                decimal valProfitCF = GetCurrentValue(rowProfitCF?.Values);
                decimal valCashCF_Begin = GetCurrentValue(rowCashCF?.Values);
                decimal valCashBS_StartYear = GetStartValue(rowCashBS?.Values);

                decimal threshold = 50;

                if (rowCashBS != null && rowCashCF != null)
                {
                    decimal diff = Math.Abs(valCashBS_StartYear - valCashCF_Begin);
                    if (diff <= threshold)
                    {
                        Log.Information($"{tag} {idInfo} | TRUC TIEP: Tien LCTT == Dau nam -> LUY KE.");
                        return true;
                    }
                    else
                    {
                        Log.Information($"{tag} {idInfo} | TRUC TIEP: Tien LCTT != Dau nam -> QUY.");
                        return false;
                    }
                }

                if (rowProfitIS != null && rowProfitCF != null)
                {
                    decimal diff = Math.Abs(valProfitIS - valProfitCF);
                    if (diff > threshold)
                    {
                        Log.Information($"{tag} {idInfo} | GIAN TIEP: LN LCTT != LN Quy -> LUY KE.");
                        return true;
                    }
                    else
                    {
                        Log.Information($"{tag} {idInfo} | GIAN TIEP: LN LCTT == LN Quy -> QUY.");
                        return false;
                    }
                }

                Log.Warning($"{tag} {idInfo} | Khong du du lieu check. Mac dinh LUY KE.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{tag} {idInfo} | Loi logic check. Mac dinh LUY KE.");
                return true;
            }
        }

        private decimal GetCurrentValue(Dictionary<string, decimal?> values)
        {
            if (values == null || values.Count == 0) return 0;
            return values.Values.FirstOrDefault() ?? 0;
        }

        private decimal GetStartValue(Dictionary<string, decimal?> values)
        {
            if (values == null || values.Count <= 1) return 0;
            return values.Values.LastOrDefault() ?? 0;
        }

        public async Task DeleteFileAsync(string fileUri)
        {
            if (string.IsNullOrWhiteSpace(fileUri))
                return;

            await _gemini.DeleteFileAsync(fileUri);
        }
    }
}