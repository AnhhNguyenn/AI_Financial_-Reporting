using BCTC.App.Services.InputServices;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Norm;
using BCTC.DataAccess.Models.Report;
using Microsoft.Extensions.Options;
using Serilog;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BCTC.App.Services.GeminiServices
{
    public partial class GeminiService
    {
        private readonly HttpClient _http;
        private readonly BctcOptions _opt;
        private static int _scanKeyIndex = 0;
        private static int _mapKeyIndex = 0;

        public static readonly ConcurrentDictionary<string, int> ScanKeyUsage = new();
        public static readonly ConcurrentDictionary<string, int> MapKeyUsage = new();

        private const string BaseUrl = "https://generativelanguage.googleapis.com";
        private readonly InputService _inputService;

        private static readonly ConcurrentDictionary<string, List<NormRow>> _normCache
            = new ConcurrentDictionary<string, List<NormRow>>();

        private readonly JsonSerializerOptions _jsonRelaxed = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly JsonSerializerOptions _jsonPretty = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public GeminiService(HttpClient http, IOptions<BctcOptions> opt, InputService inputService)
        {
            _http = http;
            _opt = opt.Value;
            _inputService = inputService;
            _http.Timeout = TimeSpan.FromMinutes(10);
        }

        public async Task<(ExtractResult, string, UsageInfo)> ScanAsync(
            CompanyReportDto reportDto,
            byte[] pdfBytes,
            string? mime,
            string? modelOverride,
            CancellationToken ct)
        {
            string key = $"{reportDto.StockCode}_{reportDto.Year}_{reportDto.ReportTerm}";
            UsageInfo usage = new();
            string apiKey = GetNextScanKey();
            string uploadedFileUri = "";

            try
            {
                Log.Information("[ScanAsync][START] Doc={Doc} Key=****{Last6}", key, apiKey.Length > 6 ? apiKey[^6..] : "xxx");
                ScanKeyUsage.AddOrUpdate(apiKey, 1, (_, old) => old + 1);

                // 1. Upload lên Google
                uploadedFileUri = await UploadFileAsync(
                    pdfBytes,
                    reportDto.Url ?? $"{key}.pdf",
                    mime ?? "application/pdf",
                    apiKey,
                    ct);

                if (string.IsNullOrEmpty(uploadedFileUri))
                    throw new Exception("Upload failed (Uri empty).");

                // 2. Poll trạng thái
                bool isActive = await WaitForFileActiveAsync(uploadedFileUri, apiKey, ct);
                if (!isActive) throw new Exception("File processing failed on Google side.");

                Log.Information("[ScanAsync][Pipeline] Starting Split-Merge Pipeline...");

                // 3. Gọi Pipeline Extractor (bên file GeminiExtractor.cs)
                var (data, pipelineUsage) = await ExecutePipelineAsync(
                        reportDto,
                        uploadedFileUri,
                        apiKey,
                        ct);

                usage = pipelineUsage;

                // 4. Post-process
                data.BusinessTypeID = reportDto.BusinessTypeID;
                if (data.MetaDB != null) FixDateFormat(data.MetaDB);

                Log.Information("[ScanAsync][SUCCESS] IS={IS} BS={BS} CF={CF} OS={OS}",
                    data.IncomeStatement?.Count ?? 0,
                    data.BalanceSheet?.Count ?? 0,
                    data.CashFlow?.Count ?? 0,
                    data.OffBalanceSheet?.Count ?? 0);

                // 5. Cleanup
                await DeleteFileAsync(uploadedFileUri, apiKey);

                return (data, modelOverride ?? _opt.Model, usage);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ScanAsync][ERROR] Doc={Doc}", key);
                if (!string.IsNullOrEmpty(uploadedFileUri)) await DeleteFileAsync(uploadedFileUri, apiKey);
                throw;
            }
        }

        private void FixDateFormat(BCTC.DataAccess.Models.MetaDB? meta)
        {
            if (meta == null || string.IsNullOrEmpty(meta.NgayKiemToan)) return;
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    meta.NgayKiemToan,
                    @"(\d{1,2})[\s\/\-]+(?:tháng)?[\s\/\-]*(\d{1,2})[\s\/\-]+(?:năm)?[\s\/\-]*(\d{4})",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    meta.NgayKiemToan = $"{match.Groups[3].Value}-{match.Groups[2].Value.PadLeft(2, '0')}-{match.Groups[1].Value.PadLeft(2, '0')}";
                }
            }
            catch { }
        }

        public async Task DeleteFileAsync(string fileUri, string apiKey)
        {
            try
            {
                string fileId = fileUri.Split('/').Last();
                var req = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/v1beta/files/{fileId}");
                req.Headers.Add("x-goog-api-key", apiKey);
                await _http.SendAsync(req);
            }
            catch { }
        }

        public async Task DeleteFileAsync(string fileUri)
        {
            if (string.IsNullOrWhiteSpace(fileUri)) return;
            await DeleteFileAsync(fileUri, GetNextScanKey());
        }

        private string GetNextScanKey()
        {
            if (_opt.ScanKeys == null || _opt.ScanKeys.Count == 0) throw new Exception("ScanKeys config missing");
            var index = Interlocked.Increment(ref _scanKeyIndex);
            return _opt.ScanKeys[index % _opt.ScanKeys.Count];
        }

        private string GetNextMapKey()
        {
            if (_opt.MapKeys == null || _opt.MapKeys.Count == 0) throw new Exception("MapKeys config missing");
            var index = Interlocked.Increment(ref _mapKeyIndex);
            return _opt.MapKeys[index % _opt.MapKeys.Count];
        }
    }
}