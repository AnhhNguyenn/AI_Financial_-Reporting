using BCTC.App.Utils;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Report;
using Serilog;
using System.Text;
using System.Text.Json;

namespace BCTC.App.Services.GeminiServices
{
    public partial class GeminiService
    {
        public async Task<(ExtractResult result, UsageInfo usage)> ExecutePipelineAsync(
            CompanyReportDto inputInfo,
            string googleFileUri,
            string apiKey,
            CancellationToken ct)
        {
            var finalResult = new ExtractResult();
            var totalUsage = new UsageInfo();

            finalResult.Company = inputInfo.CompanyName;
            finalResult.Meta = new Meta
            {
                MaCongTy = inputInfo.StockCode,
                TenCongTy = inputInfo.CompanyName,
                Nam = inputInfo.Year,
                KyBaoCao = inputInfo.ReportTerm,
                LoaiBaoCao = inputInfo.AbstractType,
                Url = inputInfo.Url,
                TrangThaiKiemDuyet = inputInfo.AuditedStatus
            };

            Log.Information("[Pipeline] 1. Routing + General Info (Scanning...)");

            var tRouter = GenerateJsonContentAsync<RouterResult>(
                googleFileUri,
                GeminiPromptBuilder.BuildRouterPrompt(),
                apiKey, ct, totalUsage,
                debugLabel: "ROUTER",
                systemPrompt: null);

            var tInfo = GenerateJsonContentAsync<AiGeneralInfo>(
                googleFileUri,
                GeminiPromptBuilder.BuildGeneralInfoPrompt(),
                apiKey, ct, totalUsage,
                debugLabel: "GENERAL_INFO",
                systemPrompt: null);

            await Task.WhenAll(tRouter, tInfo);

            var router = tRouter.Result;
            var info = tInfo.Result;

            if (info != null)
            {
                finalResult.Currency = info.Currency;
                finalResult.BaseCurrency = info.BaseCurrency;
                finalResult.CurrencyUnit = info.CurrencyUnit;
                finalResult.CashFlowMethod = info.CashFlowMethod;
                finalResult.MetaDB = info.MetaDB;
                Log.Information($"[Info] Currency: {info.Currency} | Unit: {info.CurrencyUnit}");
            }

            if (router?.tables == null || !router.tables.Any())
            {
                Log.Warning("[Pipeline] Router returned NO tables. Stopping.");
                return (finalResult, totalUsage);
            }

            foreach (var t in router.tables)
            {
                Log.Information($"[Router] Found {t.type}: Page {t.start_page} -> {t.end_page}");
            }

            Log.Information("[Pipeline] 2. Extracting Tables (Parallel Execution)");

            var tasks = new List<Task>();

            var bs = router.tables.FirstOrDefault(t => t.type == "BS" && t.end_page > 0);
            if (bs != null)
            {
                tasks.Add(ExtractTableAsync(
                    googleFileUri,
                    GeminiPromptBuilder.BuildBalanceSheetPrompt(bs.start_page, bs.end_page),
                    apiKey, ct, totalUsage,
                    rows => finalResult.BalanceSheet = rows,
                    debugLabel: "BS"
                ));
            }

            var pl = router.tables.FirstOrDefault(t => t.type == "PL" && t.end_page > 0);
            if (pl != null)
            {
                tasks.Add(ExtractTableAsync(
                    googleFileUri,
                    GeminiPromptBuilder.BuildIncomeStatementPrompt(pl.start_page, pl.end_page),
                    apiKey, ct, totalUsage,
                    rows => finalResult.IncomeStatement = rows,
                    debugLabel: "PL"
                ));
            }

            var cf = router.tables.FirstOrDefault(t => t.type == "CF" && t.end_page > 0);
            if (cf != null)
            {
                tasks.Add(ExtractCashFlowAsync(
                    googleFileUri,
                    GeminiPromptBuilder.BuildCashFlowPrompt(cf.start_page, cf.end_page),
                    apiKey, ct, totalUsage,
                    finalResult,
                    debugLabel: "CF"
                ));
            }

            var obs = router.tables.FirstOrDefault(t => t.type == "OBS" && t.end_page > 0);
            if (obs != null)
            {
                tasks.Add(ExtractTableAsync(
                    googleFileUri,
                    GeminiPromptBuilder.BuildOffBalanceSheetPrompt(obs.start_page, obs.end_page),
                    apiKey, ct, totalUsage,
                    rows => finalResult.OffBalanceSheet = rows,
                    debugLabel: "OBS"
                ));
            }

            await Task.WhenAll(tasks);

            Log.Information($"[Pipeline] 3. DONE. Total Usage: In={totalUsage.ScanInputTokens} Out={totalUsage.ScanOutputTokens}");
            return (finalResult, totalUsage);
        }

        private async Task ExtractTableAsync(
            string fileUri,
            string prompt,
            string apiKey,
            CancellationToken ct,
            UsageInfo usage,
            Action<List<Row>> assign,
            string debugLabel)
        {
            var rows = await GenerateJsonContentAsync<List<Row>>(
                fileUri,
                prompt,
                apiKey,
                ct,
                usage,
                debugLabel,
                GeminiPromptBuilder.BuildSystemInstruction());

            if (rows != null && rows.Count > 0)
            {
                Log.Information($"[{debugLabel}] Extracted {rows.Count} rows.");
                assign(rows);
            }
            else
            {
                Log.Warning($"[{debugLabel}] Extracted 0 rows or NULL.");
            }
        }

        private async Task ExtractCashFlowAsync(
            string fileUri,
            string prompt,
            string apiKey,
            CancellationToken ct,
            UsageInfo usage,
            ExtractResult result,
            string debugLabel)
        {
            var rows = await GenerateJsonContentAsync<List<Row>>(
                fileUri,
                prompt,
                apiKey,
                ct,
                usage,
                debugLabel,
                GeminiPromptBuilder.BuildSystemInstruction());

            if (rows != null && rows.Count > 0)
            {
                Log.Information($"[{debugLabel}] Extracted {rows.Count} rows.");
                result.CashFlow = rows;
            }
        }

        private async Task<T?> GenerateJsonContentAsync<T>(
            string fileUri,
            string prompt,
            string apiKey,
            CancellationToken ct,
            UsageInfo tracker,
            string debugLabel = "",
            string? systemPrompt = null) where T : class
        {
            object? systemInstruction = null;

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                };
            }

            var body = new
            {
                system_instruction = systemInstruction,
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { file_data = new { mime_type = "application/pdf", file_uri = fileUri } },
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.0,
                    response_mime_type = "application/json",
                    response_schema = BuildSchema.BuildSchemaFor<T>()
                }
            };

            int maxRetries = 3;

            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    var req = new HttpRequestMessage(
                        HttpMethod.Post,
                        $"{BaseUrl}/v1beta/models/{_opt.Model}:generateContent");

                    req.Headers.Add("x-goog-api-key", apiKey);

                    req.Content = new StringContent(
                        JsonSerializer.Serialize(body),
                        Encoding.UTF8,
                        "application/json");

                    var res = await _http.SendAsync(req, ct);

                    if ((int)res.StatusCode == 429 || (int)res.StatusCode == 503)
                    {
                        Log.Warning($"[Gemini] Rate limited ({res.StatusCode}). Retrying {i + 1}/{maxRetries}...");
                        await Task.Delay(2000 * (i + 1), ct);
                        continue;
                    }

                    res.EnsureSuccessStatusCode();

                    var json = await res.Content.ReadAsStringAsync(ct);

                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("usageMetadata", out var um))
                    {
                        lock (tracker)
                        {
                            tracker.ScanInputTokens += um.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
                            tracker.ScanOutputTokens += um.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
                        }
                    }

                    var text = doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();

                    if (string.IsNullOrWhiteSpace(text))
                        return null;

                    text = SanitizeJsonStrong(text);

                    var parsed = DeserializeSmart<T>(text);

                    if (parsed == null)
                    {
                        Log.Error($"[Gemini][{debugLabel}] Failed to deserialize JSON.");
                    }

                    return parsed;
                }
                catch (Exception ex)
                {
                    if (i == maxRetries)
                        Log.Error($"[Gemini][{debugLabel}] FINAL Error: {ex.Message}");
                    else
                        await Task.Delay(1000, ct);
                }
            }

            return null;
        }

        private string SanitizeJsonStrong(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "{}";
            var s = input.Replace("```json", "").Replace("```", "").Trim();

            int arrStart = s.IndexOf('[');
            int arrEnd = s.LastIndexOf(']');
            if (arrStart >= 0 && arrEnd > arrStart)
                return s.Substring(arrStart, arrEnd - arrStart + 1).Trim();

            int objStart = s.IndexOf('{');
            int objEnd = s.LastIndexOf('}');
            if (objStart >= 0 && objEnd > objStart)
                return s.Substring(objStart, objEnd - objStart + 1).Trim();

            return s;
        }

        private T? DeserializeSmart<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (typeof(T) == typeof(RouterResult))
                {
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("tables", out var tablesArr))
                    {
                        return JsonSerializer.Deserialize<T>(root.GetRawText(), _jsonRelaxed);
                    }

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        var listItems = JsonSerializer.Deserialize<List<TableLoc>>(root.GetRawText(), _jsonRelaxed);

                        var routerResult = new RouterResult { tables = listItems };
                        return routerResult as T;
                    }
                }

                if (typeof(T) == typeof(List<Row>))
                {
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        return JsonSerializer.Deserialize<T>(root.GetRawText(), _jsonRelaxed);
                    }

                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        string[] candidates = new[] { "items", "rows", "data", "result", "table", "tables" };
                        foreach (var key in candidates)
                        {
                            if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                            {
                                return JsonSerializer.Deserialize<T>(arr.GetRawText(), _jsonRelaxed);
                            }
                        }
                    }
                }

                return JsonSerializer.Deserialize<T>(root.GetRawText(), _jsonRelaxed);
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }
}