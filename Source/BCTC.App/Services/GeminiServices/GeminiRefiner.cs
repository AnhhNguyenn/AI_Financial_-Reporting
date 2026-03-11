using BCTC.BusinessLogic.NormLogic;
using BCTC.BusinessLogic.OcrLogic;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Norm;
using Serilog;
using System.Text;
using System.Text.Json;

namespace BCTC.App.Services.GeminiServices
{
    public partial class GeminiService
    {
        // map gemini
        public async Task<UsageInfo> RefineNormAsync(ExtractResult result, string normFolder, CancellationToken ct)
        {
            const string Tag = "[GeminiService.RefineNormAsync]";
            var usageTotal = new UsageInfo();
            string companyKey = result.Meta?.MaCongTy ?? "";

            try
            {
                if (_opt.MapKeys == null || _opt.MapKeys.Count == 0)
                    throw new InvalidOperationException("Không có MapKeys trong cấu hình!");

                int businessTypeId = result.BusinessTypeID ?? 0;

                string xmlPath = Path.Combine(normFolder, $"ReportNorm{businessTypeId}.xml");
                var allNorms = _normCache.GetOrAdd(xmlPath, path => NormMatcher.LoadNorms(path));

                string cashCode = result.CashFlowMethod?.Equals("indirect", StringComparison.OrdinalIgnoreCase) == true ? "LCGT" : "LCTT";
                var allRowsToMap = new List<Row>();
                var neededNormCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (result.IncomeStatement?.Count > 0) { allRowsToMap.AddRange(result.IncomeStatement); neededNormCodes.Add("KQ"); }
                if (result.BalanceSheet?.Count > 0) { allRowsToMap.AddRange(result.BalanceSheet); neededNormCodes.Add("CD"); }
                if (result.CashFlow?.Count > 0) { allRowsToMap.AddRange(result.CashFlow); neededNormCodes.Add(cashCode); }
                if (result.OffBalanceSheet?.Count > 0) { allRowsToMap.AddRange(result.OffBalanceSheet); neededNormCodes.Add("NB"); }
                var rowsToProcess = allRowsToMap.Where(r => !string.IsNullOrWhiteSpace(r.Item) && string.IsNullOrEmpty(r.ReportNormID)).ToList();
                var normsSubset = allNorms.Where(n => neededNormCodes.Contains(n.Code)).ToList();
                if (rowsToProcess.Count == 0 || normsSubset.Count == 0) return usageTotal;
                string apiKey = GetNextMapKey();
                string prompt = GeminiPromptBuilder.BuildNormPrompt(
                    rowsToProcess.Select(r => new RowForPrompt
                    {
                        Code = r.Code,
                        Item = r.Item,
                        ParentName = r.ParentName
                    }).ToList(),
                    normsSubset.Select(n => new NormRow
                    {
                        ReportNormID = n.ReportNormID,
                        Code = n.Code,
                        Name = n.Name,
                        PublishNormCode = n.PublishNormCode,
                        ParentName = n.ParentName
                    }).ToList(),
                    businessTypeId
                );
                var reqBody = new
                {
                    contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
                    generationConfig = new { temperature = 0, response_mime_type = "application/json" }
                };
                using var http = new HttpClient() { Timeout = TimeSpan.FromMinutes(10) };
                string url = $"{BaseUrl}/v1beta/models/{_opt.Model}:generateContent";
                string rawResponse = "";
                int maxRetries = 3;
                int delayMs = 30000;
                for (int i = 0; i <= maxRetries; i++)
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Add("x-goog-api-key", apiKey);
                    request.Content = new StringContent(JsonSerializer.Serialize(reqBody), Encoding.UTF8, "application/json");
                    var res = await http.SendAsync(request, ct);
                    rawResponse = await res.Content.ReadAsStringAsync(ct);
                    if (res.IsSuccessStatusCode) break;
                    if (((int)res.StatusCode == 429 || (int)res.StatusCode == 503) && i < maxRetries)
                    {
                        Log.Warning($"{Tag} Lỗi {res.StatusCode}. Nghỉ {delayMs / 1000}s trước khi retry {i + 1}/{maxRetries}...");
                        await Task.Delay(delayMs, ct);
                        delayMs *= 2;
                        continue;
                    }
                    throw new Exception($"Gemini API Error: {res.StatusCode} - Body: {rawResponse}");
                }
                using var doc = JsonDocument.Parse(rawResponse);
                if (doc.RootElement.TryGetProperty("usageMetadata", out var um))
                {
                    usageTotal.MapInputTokens += um.TryGetProperty("promptTokenCount", out var p) ? p.GetInt32() : 0;
                    usageTotal.MapOutputTokens += um.TryGetProperty("candidatesTokenCount", out var c) ? c.GetInt32() : 0;
                }
                string textContent = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                if (string.IsNullOrWhiteSpace(textContent)) throw new Exception("Gemini trả về text rỗng");
                var rootElement = ParseLenientOrEmpty(textContent, out string usedJson);
                JsonElement resultsArr = (rootElement.ValueKind == JsonValueKind.Object && rootElement.TryGetProperty("results", out var r))
                                         ? r : (rootElement.ValueKind == JsonValueKind.Array ? rootElement : JsonDocument.Parse("[]").RootElement);
                ApplyNormMapping(resultsArr, rowsToProcess, normsSubset);
                string wwwRoot = PathHelper.GetWwwRoot();
                //ParentValuePropagator.PropagateParentToChild(result, wwwRoot);
                Log.Information($"{Tag}[SUCCESS] Mapped rows for {companyKey}");
                return usageTotal;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{Tag}[FATAL] Error processing {companyKey}");
                throw;
            }
        }

        public static void ApplyNormMapping(JsonElement resultsArr, List<Row> allRows, List<NormRow> norms)
        {
            try
            {
                var normDict = norms
                    .GroupBy(n => n.ReportNormID)
                    .ToDictionary(g => g.Key, g => g.First());

                if (resultsArr.ValueKind != JsonValueKind.Array)
                {
                    Log.Warning("[GEMINI][MAP] kết quả trả về không phải là một Array.");
                    return;
                }

                foreach (var r in resultsArr.EnumerateArray())
                {
                    try
                    {
                        if (!r.TryGetProperty("id", out var idProp)) continue;
                        int rowIdx = -1;
                        if (idProp.ValueKind == JsonValueKind.Number) rowIdx = idProp.GetInt32();
                        else if (idProp.ValueKind == JsonValueKind.String && int.TryParse(idProp.GetString(), out int parsedIdx)) rowIdx = parsedIdx;
                        if (rowIdx < 0 || rowIdx >= allRows.Count) continue;
                        if (!r.TryGetProperty("norm", out var pNorm) || pNorm.ValueKind == JsonValueKind.Null) continue;

                        string normId = pNorm.GetString() ?? "";

                        if (!string.IsNullOrEmpty(normId) && normDict.TryGetValue(normId, out var finalNorm))
                        {
                            var targetRow = allRows[rowIdx];
                            targetRow.ReportNormID = finalNorm.ReportNormID;
                            targetRow.NormName = finalNorm.Name;
                            targetRow.PublishNormCode = finalNorm.PublishNormCode;

                            if (!string.IsNullOrEmpty(finalNorm.ParentName))
                            {
                                targetRow.ParentName = finalNorm.ParentName;
                            }
                        }
                    }
                    catch (Exception exItem)
                    {
                        Log.Warning("[GEMINI][MAP] Lỗi xử lý một dòng mapping: {Msg}", exItem.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GEMINI][MAP][ERROR] Lỗi nghiêm trọng trong ApplyNormMapping");
            }
        }
    }
}