using BCTC.App.IService;
using BCTC.App.Services.InputServices;
using BCTC.App.Utils;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Report;
using MappingReportNorm.Interfaces.Services;
using MappingReportNorm.Models;
using MappingReportNorm.Services.Providers;
using MappingReportNorm.Settings;
using Microsoft.Extensions.Options;
using Serilog;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using BCTC.DataAccess.Models.Enum;

namespace MappingReportNorm.Services
{
    public class ScannedIndicator
    {
        [JsonPropertyName("scanIndex")]
        public int ScanIndex { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        //[JsonPropertyName("fullPathName")]
        //public string FullPathName { get; set; }
        [JsonPropertyName("parentName")]
        public string ParentName { get; set; }
        [JsonPropertyName("reportNormID")]
        public int? ReportNormID { get; set; }
    }

    public class DatabaseIndicator
    {
        [JsonPropertyName("reportNormID")]
        public int ReportNormID { get; set; }

        [JsonPropertyName("standardName")]
        public string StandardName { get; set; }
        //[JsonPropertyName("aliases")]
        //public List<string> Aliases { get; set; }

        //[JsonPropertyName("category")]
        //public string Category { get; set; }
        //[JsonPropertyName("fullPathName")]
        //public string FullPathName { get; set; }
        [JsonPropertyName("parentName")]
        public string ParentName { get; set; }
    }

    public class MappingResult
    {
        [JsonPropertyName("scanIndex")]
        public int ScanIndex { get; set; }

        [JsonPropertyName("reportNormID")]
        public int? ReportNormID { get; set; }
    }
    public class ReMappingResult
    {
        [JsonPropertyName("scanIndex")]
        public int ScanIndex { get; set; }

        [JsonPropertyName("reportNormID")]
        public int ReportNormID { get; set; }

        [JsonPropertyName("sheetCode")]
        public string SheetCode { get; set; }
    }

    public class MappingResponse
    {
        [JsonPropertyName("mappings")]
        public List<MappingResult> Mappings { get; set; }
    }
    public class ReMappingGroup
    {
        [JsonPropertyName("parentID")]
        public int ParentID { get; set; }

        [JsonPropertyName("mappings")]
        public List<ReMappingResult> Mappings { get; set; } = new();
    }

    public class ReMappingResponse
    {
        [JsonPropertyName("groups")]
        public List<ReMappingGroup> Groups { get; set; } = new();
    }
    public class MappingValue
    {
        public int ReportNormID { get; set; }
        public string SheetCode { get; set; } = "";
        public Dictionary<string, decimal> Values { get; set; } = new();
    }
    public enum ModelProvider
    {
        OpenAI,
        Google
    }

    public class AIModelProviderFactory
    {
        public static IAIModelProvider CreateProvider(ModelProvider provider, MappingSettings settings)
        {
            return provider switch
            {
                ModelProvider.OpenAI => new OpenAIProvider(
                    settings.ModelOpenAI.Key,
                    settings.ModelOpenAI.Type,
                    settings.TimeoutSeconds,
                    settings.MaxRetries,
                    settings.RetryDelayMilliseconds
                ),
                ModelProvider.Google => new GoogleProvider(
                    settings.ModelGoogle.Key,
                    settings.ModelGoogle.Type,
                    settings.TimeoutSeconds,
                    settings.MaxRetries,
                    settings.RetryDelayMilliseconds
                ),
                _ => throw new ArgumentException($"Unsupported model provider: {provider}")
            };
        }
    }

    public class FinancialMappingService : IFinancialMappingService
    {
        private readonly IAIModelProvider _aiProvider;
        private readonly IFinancialService _financialService;
        private readonly MappingSettings _mappingSettings;
        private readonly ModelProvider _modelProvider;
        private readonly InputService _inputService;

        public FinancialMappingService(
            IOptions<MappingSettings> mappingSettings,
            IFinancialService financialService,
            InputService inputService
        )
        {
            _mappingSettings = mappingSettings.Value;
            _financialService = financialService;
            _inputService = inputService;

            string modelProvider = mappingSettings.Value.ModelProvider;
            if (Enum.TryParse<ModelProvider>(modelProvider, ignoreCase: true, out var provider))
            {
                _modelProvider = provider;
            }
            else
            {
                _modelProvider = ModelProvider.OpenAI;
            }

            _aiProvider = AIModelProviderFactory.CreateProvider(_modelProvider, _mappingSettings);
        }

        public async Task<MappingResponse> MapFinancialIndicatorsAsync(
            List<ScannedIndicator> scannedIndicators,
            List<DatabaseIndicator> databaseIndicators,
            int yearPeriod = 0,
            string additionalContext = "",
            string businessContext = "")
        {
            if (scannedIndicators == null || databaseIndicators == null ||
               scannedIndicators.Count == 0 || databaseIndicators.Count == 0)
            {
                return new MappingResponse
                {
                    Mappings = new List<MappingResult>()
                };
            }

            try
            {
                string systemPrompt = @"Bạn là chuyên gia Kế toán - Kiểm toán cấp cao và Kỹ sư dữ liệu - chuẩn hóa báo cáo tài chính.
                                        Nhiệm vụ cốt lõi: Mapping (ánh xạ) và Validating (thẩm định) các chỉ tiêu trên báo cáo tài chính (Scanned Indicators) vào danh sách chỉ tiêu chuẩn (Database Indicators).

                                        # ĐẶC TẢ DỮ LIỆU ĐẦU VÀO:
                                        1. **Scanned Indicators:** Danh sách phẳng, có thứ tự (`scanIndex`). Chứa: `Name`, `ParentName` (có thể rỗng hoặc sai lệch do OCR), và `ReportNormID` (có thể sai, cần thẩm định).
                                        2. **Database Indicators:** Danh mục gốc chính xác tuyệt đối.

                                        # QUY TRÌNH TƯ DUY (STEP-BY-STEP):
                                        1. **Phân tích ngữ cảnh (Contextual Analysis):**
                                           - Dựa vào `scanIndex` và thứ tự xuất hiện để xác định cấu trúc cha-con của chỉ tiêu scan (Vd: Chỉ tiêu con thường nằm ngay dưới chỉ tiêu cha hoặc thụt đầu dòng).
                                           - Xác định bản chất tài chính của chỉ tiêu (Tài sản, Nguồn vốn, Doanh thu, hay Chi phí).

                                        2. **Cơ chế Thẩm định & Mapping:**
                                           - Trường hợp Scanned Item ĐÃ CÓ `reportNormID`:
                                             + Hãy đóng vai trò 'Người phản biện': Đặt nghi vấn liệu ID đó có chính xác 100% không?
                                             + Nếu thấy sai hoặc chưa khớp ngữ nghĩa: PHẢI TÌM ID khác trong Database đúng hơn để thay thế.
                                             + Nếu thấy đúng: Giữ nguyên.
                                             (**Lưu ý**: ID chỉ đúng khi name bên **Scanned Indicators** và standardName bên **Database Indicators** khớp ngữ nghĩa kế toán).
                                           - Trường hợp Scanned Item CHƯA CÓ `reportNormID` (null):
                                             + Tìm kiếm trong Database dựa trên: Tên chỉ tiêu, Tên cha (ParentName), và Ý nghĩa, ngữ cảnh kinh tế tài chính.

                                        # TIÊU CHÍ KHỚP (MATCHING STRATEGY):
                                        BƯỚC 1 – So khớp Ngữ nghĩa (Primary Matching):
                                        - Tìm ứng viên trong Database có bản chất kế toán khớp nhất với chỉ tiêu Scan.
                                        - Ưu tiên tuyệt đối sự tương đồng về bản chất tài chính (Tài sản, Nợ phải trả, Vốn CSH, Doanh thu, Chi phí).
                                        - Nếu chỉ có một ứng viên rõ ràng → map trực tiếp.

                                        BƯỚC 2 – Phân giải khi có nhiều ứng viên:
                                        - Khi có nhiều ứng viên cùng mức khớp cao, sử dụng:
                                          + ParentName  
                                          + Cấu trúc (scanIndex)  
                                          + Ngữ cảnh xung quanh  
                                          để chọn ứng viên phù hợp nhất.

                                        BƯỚC 3 – Đảm bảo tính duy nhất toàn cục (BẮT BUỘC):
                                        - Mỗi scanIndex chỉ có tối đa 1 reportNormID, mỗi reportNormID chỉ được xuất hiện 1 lần duy nhất
                                        - Không được phép 1–N hoặc N–1 dưới mọi hình thức.
                                        - Nếu nhiều Scan cùng phù hợp với một ReportNormID:
                                          + Chỉ giữ mapping có độ phù hợp cao nhất
                                          + Các Scan còn lại phải tìm ID khác
                                          + Nếu không có ID khác phù hợp → set reportNormID = null
                                        - Nếu không thể đảm bảo 1–1 tuyệt đối → trả về null thay vì vi phạm.

                                        BƯỚC 4 – Lưu ý về ParentName:
                                        - ParentName có thể sai lệch và chỉ dùng làm yếu tố hỗ trợ khi cần phân giải.

                                        # Ràng buộc Tuyệt đối
                                        - Tuyệt đối không bịa, suy diễn hoặc tạo mới chỉ tiêu
                                        - Không map khi độ tin cậy thấp hoặc thiếu thông tin ngữ cảnh cần thiết
                                        - Chỉ sử dụng reportNormID trong danh sách chỉ tiêu chuẩn đã cho
                                        - Mapping phải là quan hệ 1–1 toàn cục: Mỗi scanIndex chỉ được gán tối đa 1 reportNormID.

                                        # Kết quả đầu ra:
                                        - Trả về kết quả đúng và đầy đủ theo JSON schema đã được định nghĩa
                                        - Không thêm giải thích, không thêm text ngoài JSON.";

                string scannedJson = JsonSerializer.Serialize(scannedIndicators, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                string databaseJson = JsonSerializer.Serialize(databaseIndicators, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                string userPrompt = $@"{additionalContext}
                                    # NHIỆM VỤ CỤ THỂ:
                                    Hãy thực hiện mapping cho dữ liệu bên dưới.
                                    Lưu ý: Bạn được phép GHI ĐÈ (Override) `reportNormID` trong dữ liệu scan nếu phát hiện sai sót. Sự chính xác là ưu tiên hàng đầu.

                                    # DỮ LIỆU ĐẦU VÀO{(yearPeriod != 0 ? $" (Năm báo cáo: {yearPeriod})" : "")}:
                                    ----------------

                                    ## 1. SCANNED INDICATORS (Cần map):
                                    <ScannedIndicators>
                                    {scannedJson}
                                    </ScannedIndicators>

                                    ## 2. DATABASE STANDARD INDICATORS (Tham chiếu):
                                    <DatabaseIndicators>
                                    {databaseJson}
                                    </DatabaseIndicators>

                                    ----------------

                                    Hãy map từng chỉ tiêu đã scan với chỉ tiêu tương ứng trong database.
                                    Nếu không tìm thấy mapping phù hợp, set `reportNormID` là null.";

                // JSON schema cho response
                object responseFormatSchema;
                if (_modelProvider == ModelProvider.OpenAI)
                {
                    responseFormatSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            mappings = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        scanIndex = new { type = "number" },
                                        reportNormID = new { type = new[] { "number", "null" } }
                                    },
                                    required = new[] { "scanIndex", "reportNormID" },
                                    additionalProperties = false
                                }
                            }
                        },
                        required = new[] { "mappings" },
                        additionalProperties = false
                    };
                }
                else
                {
                    responseFormatSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            mappings = new
                            {
                                type = "array",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        scanIndex = new { type = "number" },
                                        reportNormID = new { type = "number", nullable = true }
                                    }
                                }
                            }
                        }
                    };
                }

                var sw = Stopwatch.StartNew();
                var (responseContent, inputTokens, outputTokens, totalTokens) =
                    await _aiProvider.GetCompletionAsync(systemPrompt, userPrompt, responseFormatSchema);

                // Parse response
                var result = JsonSerializer.Deserialize<MappingResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                sw.Stop();

                Console.WriteLine($"{scannedIndicators.Count} : Execution time: {sw.ElapsedMilliseconds} ms ; inputTokens = {inputTokens} ; outputTokens = {outputTokens}");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                throw;
            }
        }
        private string NormalizeNumberCode(string code)
        {
            if (code == null)
                return null;
            string result = code.Trim().TrimStart('0');

            if (result == "")
                result = "0";
            return result;
        }

        public async Task MapByNumberCode(FinancialReportModel data)
        {
            // bỏ qua file ngân hàng
            if (data.BusinessTypeID == 3 /*|| data.BusinessTypeID == 5*/)
            {
                Log.Information("[MAP_CODE] Skip for BANK (ID=3)");
                return;
            }

            // 1. Lấy dữ liệu và xác định Template ID
            var allReportNorms = await _financialService.GetAllReportNormsAsync();
            var reportTemplateId = (int)Util.GetReportTemplateByBusinessTypeID(data.BusinessTypeID);

            bool isCashFlowDirect = string.IsNullOrWhiteSpace(data.CashFlowMethod) ||
                                    !string.Equals(data.CashFlowMethod.Trim(), "indirect", StringComparison.OrdinalIgnoreCase);

            var cashFlowComponentType = isCashFlowDirect ? ReportComponentType.CashFlowDirect : ReportComponentType.CashFlowIndirect;

            var normLookup = allReportNorms
                .Where(x => x.ReportTemplateID == reportTemplateId)
                .Select(x => new
                {
                    Key = $"{x.ReportComponentTypeID}_{NormalizeNumberCode(x.PublishNormCode)}",
                    Value = x.ReportNormID
                })
                .GroupBy(x => x.Key)
                .ToDictionary(g => g.Key, g => g.First().Value);


            void MapNormIds(IEnumerable<dynamic> items, ReportComponentType componentType)
            {
                if (items == null) return;

                int typeId = (int)componentType;

                // Lấy tất cả ID đang tồn tại realtime
                var allItems = new[]
                { data.IncomeStatement, data.BalanceSheet, data.CashFlow, data.OffBalanceSheet }
                .Where(s => s != null)
                .SelectMany(s => s)
                .Where(x => x.ReportNormID != null && (int)x.ReportNormID != 0)
                .Select(x => (int)x.ReportNormID)
                .ToHashSet();

                foreach (var item in items)
                {
                    // Sử dụng kiểm tra trực tiếp != null thay vì .HasValue để tránh lỗi Binder với dynamic
                    if (item.ReportNormID != null && (int)item.ReportNormID != 0)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.Code)) continue;

                    string normalizedItemCode = NormalizeNumberCode(item.Code);
                    string lookupKey = $"{typeId}_{normalizedItemCode}";

                    if (normLookup.TryGetValue(lookupKey, out int reportNormId))
                    {
                        if (allItems.Contains(reportNormId))
                            continue;

                        item.ReportNormID = reportNormId;
                        allItems.Add(reportNormId);
                    }
                }
            }
            MapNormIds(data.IncomeStatement, ReportComponentType.IncomeStatement);
            MapNormIds(data.BalanceSheet, ReportComponentType.BalanceSheet);
            MapNormIds(data.CashFlow, cashFlowComponentType);
            MapNormIds(data.OffBalanceSheet, ReportComponentType.OffBalanceSheet);
        }

        //map bằng dl quá khứ
        public async Task MapHistoryStrictAsync(FinancialReportModel model, CompanyReportDto reportInput)
        {
            if (reportInput.Year <= 2000) return;
            string stockCode = reportInput.StockCode;
            int prevYear = reportInput.Year - 1;
            string term = reportInput.ReportTerm;
            //string adjustAdjustedName = reportInput.IsAdjusted;
            string unitedCode = reportInput.UnitedName;
            string auditCode = reportInput.AuditedStatus;

            Log.Information("--------------------------------------------------");
            Log.Information($"[HISTORY_DEBUG] CHUẨN BỊ GỌI DB LẤY SỐ LIỆU NĂM {prevYear}");
            Log.Information($"[HISTORY_DEBUG] 1. StockCode    : '{stockCode}'");
            Log.Information($"[HISTORY_DEBUG] 2. Year (N-1)   : {prevYear}");
            Log.Information($"[HISTORY_DEBUG] 3. Term         : '{term}'");
            Log.Information($"[HISTORY_DEBUG] 4. UnitedCode   : '{unitedCode}'");
            Log.Information($"[HISTORY_DEBUG] 5. AuditCode    : '{auditCode}'");

            var historyData = await _inputService.GetHistoryDataAsync(stockCode, prevYear, term, unitedCode, null, auditCode);
            if (historyData == null || !historyData.Any()) return;

            var dbLookup = new Dictionary<decimal, List<int>>();
            foreach (var item in historyData)
            {
                if (item.Value == null || item.Value == 0) continue;
                decimal key = Math.Abs(item.Value.Value);

                if (!dbLookup.ContainsKey(key)) dbLookup[key] = new List<int>();
                if (!dbLookup[key].Contains(item.ReportNormID)) dbLookup[key].Add(item.ReportNormID);
            }

            var multipliers = new[] { 1m, 1000m, 1000000m, 1000000000m };
            var assignedIds = new HashSet<int>();

            void MapComponent(List<FinancialReportItem> items, string compName)
            {
                if (items == null || items.Count == 0) return;

                var scanValueCounts = new Dictionary<decimal, int>();

                foreach (var item in items)
                {
                    if (item.Values == null) continue;
                    foreach (var kvp in item.Values)
                    {
                        if (kvp.Key != "KyTruoc" && kvp.Key != "LuyKe_KyTruoc") continue;

                        if (kvp.Value.HasValue && kvp.Value.Value != 0)
                        {
                            decimal vAbs = Math.Abs(kvp.Value.Value);

                            if (!scanValueCounts.ContainsKey(vAbs)) scanValueCounts[vAbs] = 0;
                            scanValueCounts[vAbs]++;
                        }
                    }
                }

                foreach (var item in items)
                {
                    if (item.ReportNormID.HasValue && item.ReportNormID.Value != 0)
                    {
                        assignedIds.Add(item.ReportNormID.Value);
                        continue;
                    }
                    if (item.Values == null) continue;

                    foreach (var kvp in item.Values)
                    {
                        if (kvp.Key != "KyTruoc" && kvp.Key != "LuyKe_KyTruoc") continue;

                        if (!kvp.Value.HasValue) continue;
                        decimal scanVal = kvp.Value.Value;
                        if (scanVal == 0) continue;

                        decimal scanValAbs = Math.Abs(scanVal);

                        if (scanValueCounts.ContainsKey(scanValAbs) && scanValueCounts[scanValAbs] > 1)
                        {
                            // Log.Warning($"[SKIP] Ambiguous Value {scanVal} (Abs: {scanValAbs}) appears {scanValueCounts[scanValAbs]} times.");
                            continue;
                        }

                        bool matchFound = false;
                        foreach (var mult in multipliers)
                        {
                            decimal tryVal = scanValAbs * mult;

                            if (dbLookup.TryGetValue(tryVal, out var candidates))
                            {
                                var validCandidates = candidates.Where(id => !assignedIds.Contains(id)).ToList();

                                if (validCandidates.Count == 1)
                                {
                                    int finalId = validCandidates[0];
                                    item.ReportNormID = finalId;
                                    assignedIds.Add(finalId);

                                    Log.Information($"[HISTORY_MATCH] {compName}: '{item.Item}' " +
                                                    $"| {kvp.Key}: {scanVal:N0} (Abs: {scanValAbs:N0}) == DB: {tryVal:N0} " +
                                                    $"| -> ID: {finalId}");

                                    matchFound = true;
                                    break;
                                }
                            }
                        }
                        if (matchFound) break;
                    }
                }
            }

            MapComponent(model.BalanceSheet, "BS");
            MapComponent(model.IncomeStatement, "PL");
            MapComponent(model.CashFlow, "CF");
            MapComponent(model.OffBalanceSheet, "OBS");

            Log.Information($"[HISTORY_MAP] FINISHED Strict Mode. Total mapped: {assignedIds.Count}");
        }
    }
    public class FinancialReMappingService : IFinancialReMappingService
    {
        private readonly IAIModelProvider _aiProvider;
        private readonly IFinancialService _financialService;
        private readonly MappingSettings _mappingSettings;
        private readonly ModelProvider _modelProvider;

        public FinancialReMappingService(
            IOptions<MappingSettings> mappingSettings,
            IFinancialService financialService
        )
        {
            _mappingSettings = mappingSettings.Value;
            _financialService = financialService;

            string modelProvider = mappingSettings.Value.ModelProvider;
            if (Enum.TryParse<ModelProvider>(modelProvider, ignoreCase: true, out var provider))
                _modelProvider = provider;
            else
                _modelProvider = ModelProvider.OpenAI;

            _aiProvider = AIModelProviderFactory.CreateProvider(_modelProvider, _mappingSettings);
        }

        // =========================
        // MULTI-PARENT REMAP
        // =========================
        public async Task<ReMappingResponse> ReMapFinancialIndicatorsAsync(
            List<FormulaCandidate> candidates,
            List<ReportNorm> allReportNorms)
        {
            if (candidates == null || candidates.Count == 0)
                return new ReMappingResponse();

            string userPrompt = BuildPrompt(candidates, allReportNorms);
            if (string.IsNullOrWhiteSpace(userPrompt))
                return new ReMappingResponse();

            const string systemPrompt = "CHUYÊN GIA KẾ TOÁN - NHIỆM VỤ MAPPING ĐỐI SOÁT.";

            // ===== RESPONSE SCHEMA MULTI GROUP =====
            var responseSchema = new
            {
                type = "object",
                properties = new
                {
                    groups = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                parentID = new { type = "number" },
                                mappings = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            sheetCode = new { type = "string" },
                                            scanIndex = new { type = "number" },
                                            reportNormID = new { type = "number" }
                                        },
                                        required = new[] { "sheetCode", "scanIndex", "reportNormID" },
                                    }
                                }
                            },
                            required = new[] { "parentID", "mappings" },
                        }
                    }
                },
                required = new[] { "groups" },
            };

            var sw = Stopwatch.StartNew();

            var (json, inputTokens, outputTokens, totalTokens) =
                await _aiProvider.GetCompletionAsync(systemPrompt, userPrompt, responseSchema);

           sw.Stop();

            ReMappingResponse result;

            try
            {
                result = JsonSerializer.Deserialize<ReMappingResponse>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                ) ?? new ReMappingResponse();
            }
            catch
            {
                result = new ReMappingResponse();
            }

            Console.WriteLine(
                $"[RemapAsync] ParentGroups={result.Groups.Count}, Time={sw.ElapsedMilliseconds}ms, InputTokens={inputTokens}, OutputTokens={outputTokens}"
            );

             return result;
        }
        public static string BuildPrompt(List<FormulaCandidate> candidates, List<ReportNorm> allReportNorms)
        {
            if (candidates == null || !candidates.Any())
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("VAI TRÒ");
            sb.AppendLine("Bạn là chuyên gia kế toán tài chính chuyên phân tích báo cáo tài chính.");
            sb.AppendLine("Nhiệm vụ: Mapping ScanIndex từ PDF vào đúng ReportNormID.");

            // Kiểm tra xem có bất kỳ ParentID nào có cả 2 nguồn CURRENT và PAST không
            bool hasAnyMultiSource = candidates.Where(fc => fc.MatchedCombinations != null)
                                               .SelectMany(fc => fc.MatchedCombinations)
                                               .Any(choose => choose.ChildPDFs != null && choose.ChildPDFs.Any(x => x.Source == 1) || choose.ChildPDFs.Any(x => x.Source == 2));

            foreach (var fc in candidates)
            {
                if (fc.MatchedCombinations == null || fc.MatchedCombinations.Count == 0) continue;

                sb.AppendLine($"=== PARENTID: {fc.ParentID} - FOMULAR: {fc.ParentID} = {fc.Formula} ===");

                foreach (var choose in fc.MatchedCombinations)
                {
                    sb.AppendLine("A. CHỈ TIÊU CHUẨN (DATABASE):");

                    foreach (var id in choose.ChildIDs)
                    {
                        var info = allReportNorms.FirstOrDefault(x => x.ReportNormID == id);
                        if (info == null) continue;

                        sb.AppendLine($"- [SheetCode: {info.ReportComponentCode}] " + $"ReportNormID: {id} | Name: \"{info.Name}\"");
                    }

                    sb.AppendLine();
                    sb.AppendLine("B. CÁC TỔ HỢP PDF ỨNG VIÊN (CHỈ CHỌN 1):");

                    if (choose.ChildPDFs == null || choose.ChildPDFs.Count == 0)
                    {
                        sb.AppendLine("  (Không có tổ hợp nào)");
                        sb.AppendLine();
                        continue;
                    }

                    bool hasCurrent = choose.ChildPDFs.Any(x => x.Source == 1);
                    bool hasPast = choose.ChildPDFs.Any(x => x.Source == 2);
                    bool hasSource0 = choose.ChildPDFs.Any(x => x.Source == 0);
                    bool isMultiSource = hasCurrent && hasPast;

                    if (hasSource0)
                    {
                        // Chỉ 1 nguồn duy nhất → in bình thường
                        foreach (var cs in choose.ChildPDFs.Where(x => x.Source == 0).OrderBy(x => x.Index))
                        {
                            sb.AppendLine($"  Tổ hợp #{cs.Index}:");
                            foreach (var pdf in cs.CandidateItems)
                                sb.AppendLine($"    * ScanIndex: {pdf.ScanIndex} | Tên PDF: \"{pdf.Name}\"");
                        }
                    }
                    else if (!isMultiSource)
                    {
                        // Chỉ có 1 trong 2 nguồn (current hoặc past) → in bình thường, không cần label nguồn
                        foreach (var cs in choose.ChildPDFs.OrderBy(x => x.Source).ThenBy(x => x.Index))
                        {
                            sb.AppendLine($"  Tổ hợp #{cs.Index}:");
                            foreach (var pdf in cs.CandidateItems)
                                sb.AppendLine($"    * ScanIndex: {pdf.ScanIndex} | Tên PDF: \"{pdf.Name}\"");
                        }
                    }
                    else
                    {
                        // Có cả CURRENT và PAST → in phân biệt từng nhóm
                        sb.AppendLine("  [NGUỒN HIỆN TẠI - CURRENT]");
                        foreach (var cs in choose.ChildPDFs.Where(x => x.Source == 1).OrderBy(x => x.Index))
                        {
                            sb.AppendLine($"  Tổ hợp #{cs.Index}:");
                            foreach (var pdf in cs.CandidateItems)
                                sb.AppendLine($"    * ScanIndex: {pdf.ScanIndex} | Tên PDF: \"{pdf.Name}\"");
                        }

                        sb.AppendLine("  [NGUỒN QUÁ KHỨ - PAST]");
                        foreach (var cs in choose.ChildPDFs.Where(x => x.Source == 2).OrderBy(x => x.Index))
                        {
                            sb.AppendLine($"  Tổ hợp #{cs.Index}:");
                            foreach (var pdf in cs.CandidateItems)
                                sb.AppendLine($"    * ScanIndex: {pdf.ScanIndex} | Tên PDF: \"{pdf.Name}\"");
                        }
                    }

                    sb.AppendLine();
                }
            }

            sb.AppendLine("RÀNG BUỘC CỨNG (BẮT BUỘC TUÂN THỦ):");
            sb.AppendLine("1. Mỗi ParentID phải xuất hiện ĐÚNG 1 LẦN trong JSON output.");
            sb.AppendLine("2. Chỉ được chọn DUY NHẤT 1 tổ hợp PDF cho mỗi ParentID.");
            sb.AppendLine("3. BẮT BUỘC map đầy đủ toàn bộ ScanIndex đều có ID trong tổ hợp đã chọn.");
            sb.AppendLine("   - Không được phép gán null.");
            sb.AppendLine("   - Nếu khó → phải suy luận để chọn ID gần nghĩa nhất.");
            sb.AppendLine("4. Mapping 1–1 tuyệt đối:");
            sb.AppendLine("   - Mỗi ReportNormID chỉ xuất hiện 1 lần.");
            sb.AppendLine("   - Mỗi ScanIndex chỉ xuất hiện 1 lần.");
            sb.AppendLine("5. Trong cùng SheetCode:");
            sb.AppendLine("   - Một ScanIndex chỉ được dùng cho DUY NHẤT một ParentID.");
            sb.AppendLine("   - Nếu xảy ra cạnh tranh → chọn ParentID có độ khớp ngữ nghĩa cao hơn.");
            sb.AppendLine("   - ParentID còn lại bắt buộc trả về \"mappings\": [].");
            sb.AppendLine();
            sb.AppendLine("NGUYÊN TẮC NGỮ NGHĨA:");
            sb.AppendLine("6. Nếu tên PDF OCR sai chính tả/viết tắt → tự suy luận theo ngữ cảnh kế toán.");
            sb.AppendLine("7. Khi một dòng chứa nhiều thành phần (ví dụ: 'bán hàng, cung cấp dịch vụ và doanh thu khác'):");
            sb.AppendLine("   - Phải xác định thành phần mang tính phân loại chính.");
            sb.AppendLine("   - Thành phần có từ khóa 'khác' có độ ưu tiên phân loại CAO HƠN.");
            sb.AppendLine();
            sb.AppendLine("NGUYÊN TẮC CÔNG THỨC:");
            sb.AppendLine("8. Tránh xung đột giữa các ParentID phụ thuộc nhau (không double count).");

            // Chỉ thêm nguyên tắc nguồn nếu thực sự có ParentID nào có cả 2 nguồn
            if (hasAnyMultiSource)
            {
                sb.AppendLine();
                sb.AppendLine("NGUỒN DỮ LIỆU (CURRENT VS PAST):");
                sb.AppendLine("9. Một ParentID có thể có tổ hợp từ 2 nguồn:");
                sb.AppendLine("    - CURRENT: dữ liệu PDF hiện tại.");
                sb.AppendLine("    - PAST: dữ liệu mapping từ kỳ trước.");
                sb.AppendLine("10. Nếu CURRENT và PAST tồn tại đồng thời: Phải chọn nguồn phù hợp và đầy đủ hơn.");
            }

            sb.AppendLine();
            sb.AppendLine("==================================================");
            sb.AppendLine("ĐỊNH DẠNG JSON BẮT BUỘC:");

            sb.AppendLine("{");
            sb.AppendLine("  \"groups\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"parentID\": 123,");
            sb.AppendLine("      \"mappings\": [");
            sb.AppendLine("        { \"sheetCode\": \"CD\", \"scanIndex\": 12, \"reportNormID\": 34 }");
            sb.AppendLine("      ]");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine("CHỈ TRẢ VỀ JSON. KHÔNG GIẢI THÍCH.");

            return sb.ToString();
        }
        //public static string BuildPrompt(
        //    List<FormulaCandidate> candidates,
        //    List<ReportNorm> allReportNorms)
        //{
        //    if (candidates == null || !candidates.Any())
        //        return string.Empty;

        //    var sb = new StringBuilder();
        //    sb.AppendLine("==================================================");
        //    sb.AppendLine("VAI TRÒ");
        //    sb.AppendLine("Bạn là chuyên gia kế toán tài chính chuyên phân tích báo cáo tài chính.");
        //    sb.AppendLine("Nhiệm vụ: Mapping ScanIndex từ PDF vào đúng ReportNormID.");
        //    sb.AppendLine("==================================================");

        //    foreach (var fc in candidates)
        //    {
        //        if (fc.MatchedCombinations == null || fc.MatchedCombinations.Count == 0) continue;

        //        sb.AppendLine($"===============================");
        //        sb.AppendLine($"PARENTID: {fc.ParentID} - FOMULAR: {fc.ParentID} = {fc.Formula} ");
        //        sb.AppendLine($"===============================");

        //        foreach (var choose in fc.MatchedCombinations)
        //        {
        //            sb.AppendLine("A. CHỈ TIÊU CHUẨN (DATABASE):");

        //            foreach (var id in choose.ChildIDs)
        //            {
        //                var info = allReportNorms.FirstOrDefault(x => x.ReportNormID == id);
        //                if (info == null) continue;

        //                sb.AppendLine($"- [SheetCode: {info.ReportComponentCode}] " + $"ReportNormID: {id} | Name: \"{info.Name}\"");
        //            }

        //            sb.AppendLine();
        //            sb.AppendLine("B. CÁC TỔ HỢP PDF ỨNG VIÊN (CHỈ CHỌN 1):");

        //            foreach (var kv in choose.ChildPDFs.OrderBy(x => x.Key))
        //            {
        //                sb.AppendLine($"Tổ hợp #{kv.Key + 1}:");

        //                foreach (var pdf in kv.Value)
        //                {
        //                    sb.AppendLine($"    * ScanIndex: {pdf.ScanIndex} | Tên PDF: \"{pdf.Name}\"");
        //                }
        //                //foreach (var pdf in kv.Value)
        //                //{
        //                //    string currentMappingText = "";
        //                //    if (pdf.ReportNormID.HasValue)
        //                //    {
        //                //        var currentNorm = allReportNorms.FirstOrDefault(x => x.ReportNormID == pdf.ReportNormID.Value);
        //                //        if (currentNorm != null)
        //                //        {
        //                //            currentMappingText = $" (Đang map với {pdf.ReportNormID}: \"{currentNorm.Name}\")";
        //                //        }
        //                //    }
        //                //    sb.AppendLine($" * ScanIndex: {pdf.ScanIndex} | Tên PDF: \"{pdf.Name}\"{currentMappingText}");
        //                //}
        //            }

        //            sb.AppendLine();
        //        }
        //    }
        //    sb.AppendLine("RÀNG BUỘC CỨNG (BẮT BUỘC TUÂN THỦ):");
        //    sb.AppendLine("1. Mỗi ParentID phải xuất hiện ĐÚNG 1 LẦN trong JSON output.");
        //    sb.AppendLine("2. Chỉ được chọn DUY NHẤT 1 tổ hợp PDF cho mỗi ParentID.");
        //    sb.AppendLine("3. Mapping 1–1 tuyệt đối:");
        //    sb.AppendLine("   - Mỗi ReportNormID chỉ xuất hiện 1 lần.");
        //    sb.AppendLine("   - Mỗi ScanIndex chỉ xuất hiện 1 lần.");
        //    sb.AppendLine("4. BẮT BUỘC map đầy đủ toàn bộ ScanIndex trong tổ hợp đã chọn.");
        //    sb.AppendLine("   - Không được phép gán null.");
        //    sb.AppendLine("   - Nếu khó → phải suy luận để chọn ID gần nghĩa nhất.");
        //    sb.AppendLine("5. Trong cùng SheetCode:");
        //    sb.AppendLine("   - Một ScanIndex chỉ được dùng cho DUY NHẤT một ParentID.");
        //    sb.AppendLine("   - Nếu xảy ra cạnh tranh → chọn ParentID có độ khớp ngữ nghĩa cao hơn.");
        //    sb.AppendLine("   - ParentID còn lại bắt buộc trả về \"mappings\": [].");
        //    sb.AppendLine();
        //    sb.AppendLine("NGUYÊN TẮC NGỮ NGHĨA:");

        //    sb.AppendLine("6. Nếu tên PDF OCR sai chính tả/viết tắt → tự suy luận theo ngữ cảnh kế toán.");
        //    sb.AppendLine("7. Khi một dòng chứa nhiều thành phần (ví dụ: 'bán hàng, cung cấp dịch vụ và doanh thu khác'):");
        //    sb.AppendLine("   - Phải xác định thành phần mang tính phân loại chính.");
        //    sb.AppendLine("   - Thành phần có từ khóa 'khác' có độ ưu tiên phân loại CAO HƠN.");

        //    sb.AppendLine();
        //    sb.AppendLine("NGUYÊN TẮC CÔNG THỨC:");

        //    sb.AppendLine("8. Ưu tiên đảm bảo đúng cấu trúc công thức hơn là cố map cho đủ.");
        //    sb.AppendLine("9. Tránh xung đột giữa các ParentID phụ thuộc nhau (không double count).");

        //    sb.AppendLine("==================================================");
        //    sb.AppendLine("ĐỊNH DẠNG JSON BẮT BUỘC:");
        //    sb.AppendLine("{");
        //    sb.AppendLine("  \"groups\": [");
        //    sb.AppendLine("    {");
        //    sb.AppendLine("      \"parentID\": 123,");
        //    sb.AppendLine("      \"mappings\": [");
        //    sb.AppendLine("        { \"sheetCode\": \"CD\", \"scanIndex\": 12, \"reportNormID\": 34 }");
        //    sb.AppendLine("      ]");
        //    sb.AppendLine("    }");
        //    sb.AppendLine("  ]");
        //    sb.AppendLine("}");
        //    sb.AppendLine("CHỈ TRẢ VỀ JSON. KHÔNG GIẢI THÍCH.");


        //    sb.AppendLine();
        //    sb.AppendLine("ĐẦU RA JSON BẮT BUỘC:");
        //    sb.AppendLine("{");
        //    sb.AppendLine("  \"groups\": [");
        //    sb.AppendLine("    {");
        //    sb.AppendLine("      \"parentID\": 123,");
        //    sb.AppendLine("      \"mappings\": [");
        //    sb.AppendLine("        { \"sheetCode\": \"CD\", \"scanIndex\": 12, \"reportNormID\": 34 }");
        //    sb.AppendLine("      ]");
        //    sb.AppendLine("    }");
        //    sb.AppendLine("  ]");
        //    sb.AppendLine("}");
        //    sb.AppendLine("Chỉ trả về JSON. Không giải thích.");

        //    return sb.ToString();
        //}
    }
}
