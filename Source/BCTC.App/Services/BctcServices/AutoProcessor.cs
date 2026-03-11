using BCTC.App.IService;
using BCTC.App.Services.ChunkServices;
using BCTC.App.Services.DatabaseServices;
using BCTC.App.Services.ExcelServices;
using BCTC.App.Services.GeminiServices;
using BCTC.App.Services.InputServices;
using BCTC.App.Services.MappingCache;
using BCTC.App.Services.ScanFix;
using BCTC.App.Utils;
using BCTC.App.Utils.DataChunking.Models;
using BCTC.BusinessLogic.AutoProcessorLogic;
using BCTC.BusinessLogic.OcrLogic;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Enum;
using BCTC.DataAccess.Models.Norm;
using BCTC.DataAccess.Models.Report;
using MappingReportNorm.Interfaces.Services;
using MappingReportNorm.Models;
using MappingReportNorm.Services;
using MappingReportNorm.Utils;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NPOI.SS.UserModel;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace BCTC.App.Services.BctcServices
{
    public class AutoProcessor
    {
        private readonly IBctcservice _bctcService;
        private readonly InputService _inputService;
        private readonly string _connectionString;
        private readonly int MaxConcurrent;
        private readonly int ScanWorkers;
        private readonly int MapWorkers;
        private readonly int ImportWorkers;
        private readonly int ClipPages;
        private readonly int MaxRetryAttempts;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
        private readonly int _defaultYear;
        private readonly IFinancialService _financialService;
        private readonly IFinancialMappingService _financialMappingService;
        private readonly IFinancialReMappingService _financialReMappingService;
        private readonly INormalizationService _normalizationService;
        private readonly HashSet<int> _allowedIds;
        private readonly Dictionary<int, string> _idToKeyMap;
        private readonly DocumentCleaner _documentCleaner;
        private readonly IFileMappingCacheService _fileMappingCache;

        public AutoProcessor(
            IBctcservice bctcService,
            InputService inputService,
            IOptions<BctcOptions> opt,
            IFinancialService financialService,
            IFinancialMappingService financialMappingService,
            IFinancialReMappingService financialReMappingService,
            INormalizationService normalizationService,
            DocumentCleaner documentCleaner,
            IFileMappingCacheService fileMappingCache
            )
        {
            try
            {
                _bctcService = bctcService;
                _inputService = inputService;
                _connectionString = opt.Value.ConnectionString;
                _defaultYear = opt.Value.DefaultYear;
                var cfg = opt.Value.WorkerConfig;
                MaxConcurrent = cfg.MaxConcurrent;
                ScanWorkers = cfg.ScanWorkers;
                MapWorkers = cfg.MapWorkers;
                ImportWorkers = cfg.ImportWorkers;
                ClipPages = cfg.ClipPages;
                MaxRetryAttempts = cfg.MaxRetryAttempts;
                _financialService = financialService;
                _financialMappingService = financialMappingService;
                _financialReMappingService = financialReMappingService;
                _normalizationService = normalizationService;
                _allowedIds = new HashSet<int>();
                _idToKeyMap = new Dictionary<int, string>();
                _documentCleaner = documentCleaner;
                _fileMappingCache = fileMappingCache;


                if (cfg.BusinessRules != null && cfg.BusinessRules.Count > 0)
                {
                    foreach (var rule in cfg.BusinessRules)
                    {
                        if (!_idToKeyMap.ContainsKey(rule.Id))
                            _idToKeyMap[rule.Id] = rule.Key;
                        if (rule.Enabled)
                        {
                            _allowedIds.Add(rule.Id);
                        }
                    }
                }
                if (_allowedIds.Count == 0)
                {
                    Log.Error("[CONFIG] KHÔNG CÓ LOẠI HÌNH NÀO ĐƯỢC BẬT (Enabled=true)! TOOL SẼ KHÔNG CHẠY FILE NÀO.");
                }
                else
                {
                    Log.Information("[CONFIG] CHẾ ĐỘ WHITELIST ID: {Ids}", string.Join(", ", _allowedIds));
                }
                var root = PathHelper.GetWwwRoot();
                Log.Information("[AutoProcessor.Constructor] Initialized. WwwRoot={Root}", root);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[AutoProcessor.Constructor] FATAL");
                throw;
            }
        }

        private string GetBizKey(int? id)
        {
            if (id.HasValue && _idToKeyMap.TryGetValue(id.Value, out var key))
                return key;
            return "UNK";
        }

        public Task RunDefaultAsync() => RunAsync(_defaultYear, CancellationToken.None);

        public async Task RunAsync(int year, CancellationToken ct)
        {
            try
            {
                //TestMethod();

                Log.Information("[AutoProcessor.RunAsync] Start pipeline year={Year}.", year);
                await RunPhaseAsync(year, false, ct);

                for (int round = 1; round <= 3; round++)
                {
                    int retry = await _inputService.CountRetryAsync(ct);
                    if (retry == 0) break;
                    Log.Warning("[AutoProcessor.RunAsync] Retry round {Round} = {Count} files.", round, retry);
                    await RunPhaseAsync(year, true, ct);
                }
                Log.Information("[AutoProcessor.RunAsync] Completed pipeline year={Year}.", year);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[AutoProcessor.RunAsync] FATAL");
            }
        }

        private async Task RunPhaseAsync(int year, bool retryMode, CancellationToken ct)
        {
            try
            {
                var scanQ = Channel.CreateBounded<CompanyReportDto>(MaxConcurrent);
                var mapQ = Channel.CreateBounded<(CompanyReportDto, ExtractResult, string, UsageInfo)>(MaxConcurrent);
                var importQ = Channel.CreateBounded<(CompanyReportDto, ExtractResult, string)>(MaxConcurrent);

                var scanTasks = Enumerable.Range(0, ScanWorkers).Select(_ => ScanWorkerAsync(scanQ.Reader, mapQ.Writer, ct)).ToArray();
                var mapTasks = Enumerable.Range(0, MapWorkers).Select(_ => MapWorkerAsync(mapQ.Reader, importQ.Writer, ct)).ToArray();
                var importTasks = Enumerable.Range(0, ImportWorkers).Select(_ => ImportWorkerAsync(importQ.Reader, ct)).ToArray();

                if (!retryMode)
                    await ProducerAsync(year, scanQ.Writer, mapQ.Writer, importQ.Writer, ct);
                else
                    await ProducerRetryAsync(year, scanQ.Writer, mapQ.Writer, importQ.Writer, ct);

                scanQ.Writer.Complete(); await Task.WhenAll(scanTasks);
                mapQ.Writer.Complete(); await Task.WhenAll(mapTasks);
                importQ.Writer.Complete(); await Task.WhenAll(importTasks);
            }
            catch (Exception ex) { Log.Fatal(ex, "[AutoProcessor.RunPhaseAsync] FATAL"); }
        }

        private async Task ProducerAsync(
            int year,
            ChannelWriter<CompanyReportDto> scanWriter,
            ChannelWriter<(CompanyReportDto, ExtractResult, string, UsageInfo)> mapWriter,
            ChannelWriter<(CompanyReportDto, ExtractResult, string)> importWriter,
            CancellationToken ct)
        {
            try
            {
                var list = await _inputService.GetPendingReportsAsync(year) ?? new List<CompanyReportDto>();

                if (list.Count > 0)
                {
                    list = list.OrderBy(x => x.ProcessingPriority).ThenBy(x => x.FileInfoID).ToList();
                    Log.Information("[ProducerAsync] Đã sắp xếp {Count} file theo ProcessingPriority.", list.Count);
                }

                var statusMap = await _inputService.GetProcessingStatusMapAsync(ct);
                int pushed = 0;

                foreach (var report in list)
                {
                    if (ct.IsCancellationRequested) break;
                    int bizId = report.BusinessTypeID ?? 0;
                    if (!_allowedIds.Contains(bizId))
                    {
                        continue;
                    }
                    string bizKey = GetBizKey(bizId);

                    Log.Information("[Producer] Processing FileID={ID} Priority={P} TypeID={TId} Key={Key}",
                        report.FileInfoID, report.ProcessingPriority, bizId, bizKey);

                    try
                    {
                        string baseName = FileLogic.GetBaseFileName(report.Url, report.StockCode, report.ReportTerm, report.Year);
                        string yearStr = report.Year.ToString();
                        string termStr = string.IsNullOrWhiteSpace(report.ReportTerm) ? "UNK" : report.ReportTerm;

                        string scanJson = PathHelper.JsonScan(yearStr, termStr, bizKey, baseName + ".scan.json");
                        string mapJson = PathHelper.JsonMap(yearStr, termStr, bizKey, baseName + ".map.json");

                        int currentStatus = 0;
                        if (statusMap.TryGetValue(report.FileInfoID, out var st)) currentStatus = st;

                        if (File.Exists(mapJson))
                        {
                            var json = await File.ReadAllTextAsync(mapJson, ct);
                            var mapped = JsonSerializer.Deserialize<ExtractResult>(json);
                            if (mapped != null)
                            {
                                await importWriter.WriteAsync((report, mapped, baseName), ct);
                                if (currentStatus != 1 && currentStatus != 4) await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.Mapped, ct);
                                continue;
                            }
                        }

                        if (File.Exists(scanJson) /*&& currentStatus == 2*/)
                        {
                            var json = await File.ReadAllTextAsync(scanJson, ct);
                            var scanned = JsonSerializer.Deserialize<ExtractResult>(json);
                            if (scanned != null)
                            {
                                await mapWriter.WriteAsync((report, scanned, baseName, UsageInfo.Empty), ct);
                                continue;
                            }
                        }

                        if (currentStatus == 1 || currentStatus == 4) continue;
                        if (report.ProcessingStatus > 4 || report.ProcessingStatus < 0) await TryUpdateProcessingStatusAsync(report, 0, ct);

                        await scanWriter.WriteAsync(report, ct);
                        pushed++;
                        await Task.Delay(100, ct);
                    }
                    catch (Exception exItem)
                    {
                        Log.Error(exItem, "[Producer] Error enqueue FileInfoID={ID}", report.FileInfoID);
                        await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.DownloadError, ct);
                    }
                }
            }
            catch (Exception ex) { Log.Fatal(ex, "[Producer] FATAL"); }
        }

        private async Task ProducerRetryAsync(
            int year,
            ChannelWriter<CompanyReportDto> scanWriter,
            ChannelWriter<(CompanyReportDto, ExtractResult, string, UsageInfo)> mapWriter,
            ChannelWriter<(CompanyReportDto, ExtractResult, string)> importWriter,
            CancellationToken ct)
        {
            try
            {
                var failed = await _inputService.GetRetryReportsAsync(year) ?? new List<CompanyReportDto>();
                failed = failed.OrderBy(x => x.ProcessingPriority).ToList();

                foreach (var report in failed)
                {
                    if (ct.IsCancellationRequested) break;

                    int bizId = report.BusinessTypeID ?? 0;
                    if (!_allowedIds.Contains(bizId)) continue;

                    await EnqueueRetryFileAsync(report, scanWriter, mapWriter, importWriter, ct);
                }
            }
            catch (Exception ex) { Log.Fatal(ex, "[ProducerRetry] FATAL"); }
        }

        private async Task EnqueueRetryFileAsync(CompanyReportDto report, ChannelWriter<CompanyReportDto> scanWriter, ChannelWriter<(CompanyReportDto, ExtractResult, string, UsageInfo)> mapWriter, ChannelWriter<(CompanyReportDto, ExtractResult, string)> importWriter, CancellationToken ct)
        {
            try
            {
                string baseName = FileLogic.GetBaseFileName(report.Url, report.StockCode, report.ReportTerm, report.Year);
                string yearStr = report.Year.ToString();
                string termStr = string.IsNullOrWhiteSpace(report.ReportTerm) ? "UNK" : report.ReportTerm;

                int bizId = report.BusinessTypeID ?? 0;
                string bizKey = GetBizKey(bizId);

                string scanJson = PathHelper.JsonScan(yearStr, termStr, bizKey, baseName + ".scan.json");
                string mapJson = PathHelper.JsonMap(yearStr, termStr, bizKey, baseName + ".map.json");

                if (File.Exists(mapJson))
                {
                    var json = await File.ReadAllTextAsync(mapJson, ct);
                    var mapped = JsonSerializer.Deserialize<ExtractResult>(json);
                    if (mapped != null) { await importWriter.WriteAsync((report, mapped, baseName), ct); return; }
                }

                if (File.Exists(scanJson))
                {
                    var json = await File.ReadAllTextAsync(scanJson, ct);
                    var scanned = JsonSerializer.Deserialize<ExtractResult>(json);
                    if (scanned != null) { await mapWriter.WriteAsync((report, scanned, baseName, UsageInfo.Empty), ct); return; }
                }

                await _inputService.UpdateProcessingStatusAsync(report.FileInfoID, 0, ct);
                await scanWriter.WriteAsync(report, ct);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EnqueueRetry] FAIL FileInfoID={ID}", report.FileInfoID);
                await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.DownloadError, ct);
            }
        }

        private async Task ScanWorkerAsync(ChannelReader<CompanyReportDto> reader, ChannelWriter<(CompanyReportDto, ExtractResult, string, UsageInfo)> writer, CancellationToken ct)
        {
            await foreach (var report in reader.ReadAllAsync(ct))
            {
                string baseName = "UNK";
                string? downloadPath = null;
                string? clippedPath = null;
                string? finalPathToScan = null;

                try
                {
                    baseName = FileLogic.GetBaseFileName(report.Url, report.StockCode, report.ReportTerm, report.Year);
                    string yearStr = report.Year.ToString();
                    string termStr = report.ReportTerm ?? "UNK";

                    int bizId = report.BusinessTypeID ?? 0;
                    string bizKey = GetBizKey(bizId);

                    string scanJson = PathHelper.JsonScan(yearStr, termStr, bizKey, baseName + ".scan.json");

                    if (File.Exists(scanJson))
                    {
                        var cached = JsonSerializer.Deserialize<ExtractResult>(await File.ReadAllTextAsync(scanJson, ct));
                        if (cached != null)
                        {
                            await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.Scanned, ct);
                            await writer.WriteAsync((report, cached, baseName, UsageInfo.Empty), ct);
                            continue;
                        }
                    }

                    downloadPath = await DownloadPdfAsync(report, bizKey, ct);
                    if (downloadPath == null) { await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.DownloadError, ct); continue; }

                    // 1. Cắt File
                    clippedPath = PathHelper.PdfChunk(yearStr, termStr, bizKey, baseName + "_clip.pdf");
                    ClipPdf(downloadPath, clippedPath, ClipPages);
                    if (!File.Exists(clippedPath)) throw new FileNotFoundException("Lỗi cắt file PDF");

                    // 2. [QUAN TRỌNG] LÀM SẠCH FILE TRƯỚC KHI ĐỌC
                    // _documentCleaner sẽ kiểm tra: nếu bẩn -> tạo file mới, nếu sạch -> trả về file cũ
                    Log.Information($"[AutoProcessor] Cleaning PDF: {clippedPath}");
                    //finalPathToScan = _documentCleaner.CleanAndSavePdf(clippedPath);
                    finalPathToScan = (clippedPath);

                    // 3. Đọc bytes từ file ĐÃ XỬ LÝ (finalPathToScan)
                    byte[] clippedBytes = await File.ReadAllBytesAsync(finalPathToScan, ct);

                    // 4. Gửi đi Scan
                    var scanResult = await TryExtractWithRetryAsync(baseName, clippedBytes, report, ct);
                    if (scanResult == null) { await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.ScanError, ct); continue; }

                    var data = scanResult.Value.data;
                    MetaLogic.MergeMeta(data, report);


                    if (data == null || (data.IncomeStatement?.Count == 0 && data.BalanceSheet?.Count == 0 && data.CashFlow?.Count == 0))
                    {
                        await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.ScanError, ct);
                    }
                    else
                    {
                        await SaveJsonAsync(scanJson, data);
                        await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.Scanned, ct);
                        await writer.WriteAsync((report, data, baseName, scanResult.Value.usage), ct);
                    }
                }
                catch (Exception ex) { Log.Error(ex, "[Scan] Error ID={ID}", report.FileInfoID); await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.ScanError, ct); }
                finally
                {
                    if (downloadPath != null && File.Exists(downloadPath)) File.Delete(downloadPath);
                    if (clippedPath != null && File.Exists(clippedPath)) File.Delete(clippedPath);

                    if (!string.IsNullOrEmpty(finalPathToScan) && finalPathToScan != clippedPath && File.Exists(finalPathToScan))
                    {
                        try { File.Delete(finalPathToScan); } catch { }
                    }
                }
            }
        }

        private async Task MapWorkerAsync(ChannelReader<(CompanyReportDto, ExtractResult, string, UsageInfo)> reader, ChannelWriter<(CompanyReportDto, ExtractResult, string)> writer, CancellationToken ct)
        {
            await foreach (var (report, scanned, baseName, usage) in reader.ReadAllAsync(ct))
            {
                try
                {
                    int bizId = report.BusinessTypeID ?? 0;
                    string bizKey = GetBizKey(bizId);
                    string mapJson = PathHelper.JsonMap(report.Year.ToString(), report.ReportTerm ?? "UNK", bizKey, baseName + ".map.json");

                    if (File.Exists(mapJson))
                    {
                        var cached = JsonSerializer.Deserialize<ExtractResult>(await File.ReadAllTextAsync(mapJson, ct));
                        if (cached != null && HasMappedData(cached))
                        {
                            await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.Mapped, ct);
                            await writer.WriteAsync((report, cached, baseName), ct);
                            continue;
                        }
                    }

                    if (scanned == null || !await TryMapWithRetryAsync(scanned, report, baseName, ct))
                    {
                        await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.MapError, ct); continue;
                    }

                    await SaveJsonAsync(mapJson, scanned);
                    if (!HasMappedData(scanned)) { await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.MapError, ct); continue; }

                    await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.Mapped, ct);
                    await writer.WriteAsync((report, scanned, baseName), ct);
                }
                catch (Exception ex) { Log.Error(ex, "[Map] Error ID={ID}", report.FileInfoID); await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.MapError, ct); }
            }
        }

        private async Task ImportWorkerAsync(ChannelReader<(CompanyReportDto report, ExtractResult mapped, string baseName)> reader, CancellationToken ct)
        {
            await foreach (var (report, mapped, baseName) in reader.ReadAllAsync(ct))
            {
                try
                {
                    if (mapped == null) { await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.ImportError, ct); continue; }
                    var finalResults = _bctcService.SplitCashFlowResult(mapped);

                    foreach (var item in finalResults)
                    {
                        if (item.IncomeStatement.Count == 0 && item.BalanceSheet.Count == 0 && item.CashFlow.Count == 0) continue;
                        string kyMoi = item.Meta?.KyBaoCao ?? report.ReportTerm ?? "UNK";
                        string finalName = CreateProcessedFileName(baseName, report.ReportTerm ?? "UNK", kyMoi);
                        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

                        int bizId = item.BusinessTypeID ?? report.BusinessTypeID ?? 0;
                        string bizKey = GetBizKey(bizId);

                        string folderPath = Path.Combine(PathHelper.GetWwwRoot(), "json", "processed", report.Year.ToString(), kyMoi, bizKey);
                        Directory.CreateDirectory(folderPath);

                        string jsonPath = Path.Combine(folderPath, finalName + ".processed.json");
                        await File.WriteAllTextAsync(jsonPath, json, ct);

                        string excelPath = Path.Combine(folderPath, finalName + ".xls");
                        try { ExcelExporter.Export(JsonDocument.Parse(json).RootElement, excelPath, new List<NormRow>(), item); }
                        catch (Exception exExcel) { Log.Error(exExcel, "[Import] Error Excel: {Path}", excelPath); }


                        //await SqlImportHelper.ImportToDatabaseAsync(item, _connectionString);
                    }
                    await SqlImportHelper.MarkFileAsProcessedAsync(report.FileInfoID, _connectionString);
                    await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.Completed, ct);
                }
                catch (Exception ex) { Log.Error(ex, "[Import] Error ID={ID}", report.FileInfoID); await TryUpdateProcessingStatusAsync(report, FileProcessingStatusEnum.ImportError, ct); }
            }
        }

        private static async Task<string?> DownloadPdfAsync(CompanyReportDto report, string bizKey, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(report.Url)) return null;
                using var http = new HttpClient();
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                string safeUrl = report.Url.Replace(" ", "%20");
                byte[] bytes;
                try { bytes = await http.GetByteArrayAsync(safeUrl, ct); } catch { return null; }
                string year = report.Year.ToString();
                string term = string.IsNullOrWhiteSpace(report.ReportTerm) ? "UNK" : report.ReportTerm;
                string ext = Path.GetExtension(safeUrl).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) ext = ".pdf";
                string shortName = $"{report.StockCode}_{report.ReportTerm}_{report.Year}".Replace(" ", "_");
                string downloadPath = PathHelper.PdfDownload(year, term, bizKey, shortName + ext);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);
                    await File.WriteAllBytesAsync(downloadPath, bytes, ct);
                }
                catch { return null; }
                if (ext == ".pdf") return downloadPath;
                string extractFolder = Path.Combine(Path.GetDirectoryName(downloadPath)!, shortName + "_extracted");
                string? pdfPath = FileLogic.UnpackArchive(downloadPath, extractFolder);

                if (!string.IsNullOrEmpty(pdfPath))
                {
                    Log.Information("[DownloadPdfAsync] Đã giải nén thành công: {Rar} -> {Pdf}", downloadPath, pdfPath);
                    return pdfPath;
                }
                Log.Warning("[DownloadPdfAsync] Không tìm thấy PDF trong file nén: {Path}", downloadPath);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DownloadPdfAsync] Lỗi tải/giải nén: {Url}", report.Url);
                return null;
            }
        }

        private bool HasMappedData(ExtractResult res)
        {
            if (res == null) return false;

            bool hasIS = res.IncomeStatement?.Any(r => !string.IsNullOrEmpty(r.ReportNormID)) ?? false;
            bool hasBS = res.BalanceSheet?.Any(r => !string.IsNullOrEmpty(r.ReportNormID)) ?? false;
            bool hasCF = res.CashFlow?.Any(r => !string.IsNullOrEmpty(r.ReportNormID)) ?? false;

            return hasIS || hasBS || hasCF;
        }

        private static string CreateProcessedFileName(string baseName, string oldTerm, string newTerm)
        {
            if (string.Equals(oldTerm, newTerm, StringComparison.OrdinalIgnoreCase))
            {
                return baseName;
            }

            try
            {
                string newName = System.Text.RegularExpressions.Regex.Replace(baseName,
                    oldTerm, newTerm,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (newName == baseName)
                {
                    return $"{baseName}_{newTerm}";
                }

                return newName;
            }
            catch
            {
                return $"{baseName}_{newTerm}";
            }
        }

        private static async Task SaveJsonAsync(string path, ExtractResult data)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var opt = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string json = JsonSerializer.Serialize(data, opt);
                await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
                Log.Information("[AutoProcessor] Đã lưu thành công JSON vào: {Path}", path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AutoProcessor.SaveJson] KHÔNG THỂ LƯU FILE: {Path}", path);
            }
        }

        private async Task TryUpdateProcessingStatusAsync(CompanyReportDto report, FileProcessingStatusEnum status, CancellationToken ct)
        {
            try
            {
                if (report.FileInfoID <= 0)
                    return;

                try
                {
                    await _inputService.UpdateProcessingStatusAsync(report.FileInfoID, (int)status, ct);
                }
                catch (Exception exUpdate)
                {
                    Log.Warning(exUpdate, "[AutoProcessor.TryUpdateProcessingStatus] Update fail FileInfoID={ID}", report.FileInfoID);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[AutoProcessor.TryUpdateProcessingStatus] FAIL FileInfoID={ID}", report.FileInfoID);
            }
        }

        public static string ClipPdf(string inputPath, string outputPath, int pages)
        {
            using (var fs = File.OpenRead(inputPath))
            {
                string temp = PdfChunker.ExtractFirstPages(fs, pages, Path.GetFileNameWithoutExtension(inputPath) + "_tmp");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.Copy(temp, outputPath, true);
            }

            return outputPath;
        }

        private async Task<(ExtractResult? data, string model, UsageInfo usage)?> TryExtractWithRetryAsync(string baseName, byte[] clippedBytes, CompanyReportDto report, CancellationToken ct)
        {
            int attempt = 0;
            Exception? lastEx = null;

            while (attempt < MaxRetryAttempts)
            {
                attempt++;

                try
                {
                    Log.Information("[AutoProcessor.ScanWorker] Extract attempt {Attempt} FileInfoID={ID}", attempt, report.FileInfoID);

                    var result = await _bctcService.ExtractAsync(
                        baseName,
                        clippedBytes,
                        "application/pdf",
                        PathHelper.GetWwwRoot(),
                        null,
                        false,
                        report.BusinessTypeID,
                        ct
                    );


                    return (result.data, result.modelUsed, result.usage);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Log.Error(ex, "[AutoProcessor.ScanWorker] OCR fail attempt {Attempt} FileInfoID={ID}", attempt, report.FileInfoID);

                    if (attempt >= MaxRetryAttempts)
                        break;

                    try { await Task.Delay(RetryDelay, ct); } catch { }
                }
            }

            if (lastEx != null)
                Log.Error(lastEx, "[AutoProcessor.ScanWorker] OCR final fail FileInfoID={ID}", report.FileInfoID);

            return null;
        }

        //private void TestMethod()
        //{
        //    var values = new Dictionary<int, decimal?>
        //    {
        //        {4383, -1800834 },
        //        {4384, 1002 }
        //    };
        //var sysVars = new Dictionary<string, object>
        //{
        //    { "StockCode", "ACB" },
        //    { "4383", values[4383]},
        //    { "Market", 10 }
        //}; 
        //    var formulas = new List<FormulaDefinition>
        //    {
        //        new FormulaDefinition
        //        {
        //            ReportNormID = 4384,
        //            Formula = "IF(#4383 < 0, 0 - @4384, @4384)"
        //        },
        //        new FormulaDefinition
        //        {
        //            ReportNormID = 4383,
        //            Formula = "ABS(@4383)"
        //        }
        //    };

        //    var executor = new FormulaExecutor();

        //    try
        //    {
        //        executor.Execute(formulas, values, sysVars);
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("Fatal Error: " + ex.Message);
        //    }
        //}//private void TestMethod()
        //{
        //    var values = new Dictionary<int, decimal?>
        //    {
        //        {4383, -1800834 },
        //        {4384, 1002 }
        //    };

        //    var sysVars = new Dictionary<string, object>
        //    {
        //        { "StockCode", "ACB" },
        //        { "4383", values[4383]},
        //        { "Market", 10 }
        //    };

        //    var formulas = new List<FormulaDefinition>
        //    {
        //        new FormulaDefinition
        //        {
        //            ReportNormID = 4384,
        //            Formula = "IF(#4383 < 0, 0 - @4384, @4384)"
        //        },
        //        new FormulaDefinition
        //        {
        //            ReportNormID = 4383,
        //            Formula = "ABS(@4383)"
        //        }
        //    };

        //    var executor = new FormulaExecutor();

        //    try
        //    {
        //        executor.Execute(formulas, values, sysVars);
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("Fatal Error: " + ex.Message);
        //    }
        //}

        private async Task<bool> TryMapWithRetryAsync(
                    ExtractResult scanned,
                    CompanyReportDto report,
                    string baseName,
                    CancellationToken ct)
        {
            try
            {
                FinancialReportModel model = new FinancialReportModel();

                model.BusinessTypeID = scanned.BusinessTypeID ?? report.BusinessTypeID ?? 0;
                model.BusinessTypeName = scanned.BusinessTypeName ?? report.BusinessTypeName;
                model.Company = scanned.Company ?? report.CompanyName;
                model.Currency = scanned.Currency;
                model.BaseCurrency = scanned.BaseCurrency;
                model.CurrencyUnit = scanned.CurrencyUnit;
                model.CashFlowMethod = scanned.CashFlowMethod;

                model.IncomeStatement = MapRowsToItems(scanned.IncomeStatement);
                model.BalanceSheet = MapRowsToItems(scanned.BalanceSheet);
                model.CashFlow = MapRowsToItems(scanned.CashFlow);
                model.OffBalanceSheet = MapRowsToItems(scanned.OffBalanceSheet);

                model.Meta = new Meta
                {
                    MaCongTy = scanned.Meta?.MaCongTy,
                    TenCongTy = report.CompanyName ?? scanned.Meta?.TenCongTy,
                    Nam = report.Year != 0 ? report.Year : (scanned.Meta?.Nam ?? 0),
                    KyBaoCao = report.ReportTerm ?? scanned.Meta?.KyBaoCao,
                    Url = report.Url ?? scanned.Meta?.Url,
                    LoaiBaoCao = report.AbstractType ?? scanned.Meta?.LoaiBaoCao,
                    TinhChatBaoCao = report.UnitedName ?? scanned.Meta?.TinhChatBaoCao,
                    TrangThaiKiemDuyet = report.AuditedStatus ?? scanned.Meta?.TrangThaiKiemDuyet,
                    ThuocTinhKhac = scanned.Meta?.ThuocTinhKhac,
                    NgayCongBoBCTC = scanned.Meta?.NgayCongBoBCTC
                };

                Util.SetScanIndex(model.BalanceSheet);
                Util.SetScanIndex(model.IncomeStatement);
                Util.SetScanIndex(model.CashFlow);
                Util.SetScanIndex(model.OffBalanceSheet);

                bool isCashFlowDirect = string.IsNullOrWhiteSpace(model.CashFlowMethod) ||
                                        !string.Equals(model.CashFlowMethod.Trim(), "indirect", StringComparison.OrdinalIgnoreCase);

                ReportComponentType reportComponent_CashFlow = isCashFlowDirect
                    ? ReportComponentType.CashFlowDirect
                    : ReportComponentType.CashFlowIndirect;

                var reportTemplate = Util.GetReportTemplateByBusinessTypeID(model.BusinessTypeID);

                // MAP BẰNG DỮ LIỆU QUÁ KHỨ (STRICT MODE) ---
                try
                {
                    // Hàm này đã được sửa ở FinancialMappingService để xử lý trùng lặp
                    await _financialMappingService.MapHistoryStrictAsync(model, report);
                }
                catch (Exception ex)
                {
                    Log.Warning("[AutoProcessor] Lỗi History Map: " + ex.Message);
                }

                // Map Code Cứng
                await _financialMappingService.MapByNumberCode(model);

                // Prepare Data
                var dataScan_BalanceSheet = Util.CreateScanItems(model.BalanceSheet);
                var dataScan_IncomeStatement = Util.CreateScanItems(model.IncomeStatement);
                var dataScan_CashFlow = Util.CreateScanItems(model.CashFlow);
                var dataScan_OffBalanceSheet = Util.CreateScanItems(model.OffBalanceSheet);

                var dbTask_BalanceSheet = _normalizationService.ReportNorms_Database(reportTemplate, ReportComponentType.BalanceSheet);
                var dbTask_IncomeStatement = _normalizationService.ReportNorms_Database(reportTemplate, ReportComponentType.IncomeStatement);
                var dbTask_CashFlow = _normalizationService.ReportNorms_Database(reportTemplate, reportComponent_CashFlow);
                var dbTask_OffBalanceSheet = _normalizationService.ReportNorms_Database(reportTemplate, ReportComponentType.OffBalanceSheet);

                var scanTask_BalanceSheet = _normalizationService.ReportNorms_Scan(dataScan_BalanceSheet, reportTemplate, ReportComponentType.BalanceSheet);
                var scanTask_IncomeStatement = _normalizationService.ReportNorms_Scan(dataScan_IncomeStatement, reportTemplate, ReportComponentType.IncomeStatement);
                var scanTask_CashFlow = _normalizationService.ReportNorms_Scan(dataScan_CashFlow, reportTemplate, reportComponent_CashFlow);
                var scanTask_OffBalanceSheet = _normalizationService.ReportNorms_Scan(dataScan_OffBalanceSheet, reportTemplate, ReportComponentType.OffBalanceSheet);

                await Task.WhenAll(
                    dbTask_BalanceSheet, dbTask_IncomeStatement, dbTask_CashFlow, dbTask_OffBalanceSheet,
                    scanTask_BalanceSheet, scanTask_IncomeStatement, scanTask_CashFlow, scanTask_OffBalanceSheet
                );

                var reportNorms_BalanceSheet = await dbTask_BalanceSheet;
                var reportNorms_IncomeStatement = await dbTask_IncomeStatement;
                var reportNorms_CashFlow = await dbTask_CashFlow;
                var reportNorms_OffBalanceSheet = await dbTask_OffBalanceSheet;

                var scannedIndicators_BalanceSheet_Normalize = await scanTask_BalanceSheet;
                var scannedIndicators_IncomeStatement_Normalize = await scanTask_IncomeStatement;
                var scannedIndicators_CashFlow_Normalize = await scanTask_CashFlow;
                var scannedIndicators_OffBalanceSheet_Normalize = await scanTask_OffBalanceSheet;

                var (scannedIndicators_BalanceSheet, databaseIndicators_BalanceSheet) =
                    Util.PrepareIndicators(scannedIndicators_BalanceSheet_Normalize, reportNorms_BalanceSheet);

                var (scannedIndicators_IncomeStatement, databaseIndicators_IncomeStatement) =
                    Util.PrepareIndicators(scannedIndicators_IncomeStatement_Normalize, reportNorms_IncomeStatement);

                var (scannedIndicators_CashFlow, databaseIndicators_CashFlow) =
                    Util.PrepareIndicators(scannedIndicators_CashFlow_Normalize, reportNorms_CashFlow);

                var (scannedIndicators_OffBalanceSheet, databaseIndicators_OffBalanceSheet) =
                    Util.PrepareIndicators(scannedIndicators_OffBalanceSheet_Normalize, reportNorms_OffBalanceSheet);

                // Chạy map AI
                var t1 = _financialMappingService.MapFinancialIndicatorsAsync(scannedIndicators_BalanceSheet, databaseIndicators_BalanceSheet, model.Meta.Nam);
                await Task.Delay(3000, ct);
                var t2 = _financialMappingService.MapFinancialIndicatorsAsync(scannedIndicators_IncomeStatement, databaseIndicators_IncomeStatement, model.Meta.Nam);
                await Task.Delay(3000, ct);
                var t3 = _financialMappingService.MapFinancialIndicatorsAsync(scannedIndicators_CashFlow, databaseIndicators_CashFlow, model.Meta.Nam);
                await Task.Delay(3000, ct);
                var t4 = _financialMappingService.MapFinancialIndicatorsAsync(scannedIndicators_OffBalanceSheet, databaseIndicators_OffBalanceSheet, model.Meta.Nam);

                var mappingResults = await Task.WhenAll(t1, t2, t3, t4);

                var map_BalanceSheet = mappingResults[0];
                var map_IncomeStatement = mappingResults[1];
                var map_CashFlow = mappingResults[2];
                var map_OffBalanceSheet = mappingResults[3];

                ApplyMappingToSetReportNormID(model.BalanceSheet, map_BalanceSheet.Mappings);
                ApplyMappingToSetReportNormID(model.IncomeStatement, map_IncomeStatement.Mappings);
                ApplyMappingToSetReportNormID(model.CashFlow, map_CashFlow.Mappings);
                ApplyMappingToSetReportNormID(model.OffBalanceSheet, map_OffBalanceSheet.Mappings);

                var formulas = new List<FormulaDefinition>();
                try
                {
                    string formulaFileName = model.BusinessTypeID switch { 1 => "Formula_CP.json", 2 => "Formula_CK.json", 3 => "Formula_NH.json", 5 => "Formula_BH.json", _ => "Formula_CP.json" };
                    var pathFormula = Path.Combine(AppContext.BaseDirectory, "wwwroot", "Formula", formulaFileName);
                    if (File.Exists(pathFormula))
                    {
                        var rawDataFormula = JsonSerializer.Deserialize<Dictionary<string, List<FormulaDefinitionRaw>>>(await File.ReadAllTextAsync(pathFormula));
                        if (rawDataFormula != null)
                        {
                            foreach (var (sheetCode, items) in rawDataFormula)
                            {
                                foreach (var item in items)
                                {
                                    formulas.Add(new FormulaDefinition { ReportNormID = int.Parse(item.ReportNorm.ToString()), Formula = item.Formula, SheetCode = sheetCode });
                                }
                            }
                        }
                    }
                    var pathFormula_Special = Path.Combine(AppContext.BaseDirectory, "wwwroot", "Formula", "Formula_Special.json");
                    if (File.Exists(pathFormula_Special))
                    {
                        var rawSpecial = JsonSerializer.Deserialize<List<FormulaDefinitionRaw>>(await File.ReadAllTextAsync(pathFormula_Special));
                        if (rawSpecial != null)
                        {
                            formulas.AddRange(rawSpecial.Select(i => new FormulaDefinition
                            {
                                ReportNormID = i.ReportNorm != null ? int.Parse(i.ReportNorm.ToString()) : (int?)null,
                                Formula = i.Formula,
                                SheetCode = "SPECIAL"
                            }));
                        }
                    }
                }
                catch (Exception ex) { Log.Error(ex, "[Formula] Error reading formula config"); }

                long ParseCurrencyUnit(FinancialReportModel res)
                {
                    if (res.CurrencyUnit.HasValue && res.CurrencyUnit.Value > 0) return res.CurrencyUnit.Value;
                    string text = (res.Currency ?? res.Meta?.ThuocTinhKhac ?? "").ToLower().Trim();
                    if (string.IsNullOrEmpty(text)) return 1;
                    if (text.Contains("tỷ") || text.Contains("billion")) return 1_000_000_000;
                    if (text.Contains("triệu") || text.Contains("million")) return 1_000_000;
                    if (text.Contains("nghìn") || text.Contains("ngàn") || text.Contains("thousand")) return 1_000;
                    return 1;
                }
                long unit = ParseCurrencyUnit(model);

                if (unit == 1 && model.CurrencyUnit == null)
                    Log.Warning($"[Unit Check] Currency unit not specified. Default is 1. Original string: {model.Currency}");

                //ngoại lệ ko nhân thêm unit
                var nonScaledNormIds = new HashSet<int>
                {
                    4381, 3203, //NH, BH
                    4588, 5480, //CK
                    2215 //CP
                };

                void ConvertUnitModel(FinancialReportModel model, long unit)
                {
                    if (model == null || unit == 1)
                        return;

                    var rows = Enumerable.Empty<FinancialReportItem>()
                        .Concat(model.BalanceSheet ?? Enumerable.Empty<FinancialReportItem>())
                        .Concat(model.IncomeStatement ?? Enumerable.Empty<FinancialReportItem>())
                        .Concat(model.CashFlow ?? Enumerable.Empty<FinancialReportItem>())
                        .Concat(model.OffBalanceSheet ?? Enumerable.Empty<FinancialReportItem>());

                    foreach (var row in rows)
                    {
                        if (row.Values == null)
                            continue;

                        if (row.ReportNormID.HasValue && nonScaledNormIds.Contains(row.ReportNormID.Value))
                            continue;

                        var keys = row.Values.Keys.ToList();

                        foreach (var key in keys)
                        {
                            if (row.Values[key].HasValue)
                            {
                                row.Values[key] *= unit;
                            }
                        }
                    }
                }
                ConvertUnitModel(model, unit);
                RecheckAutoAddHelper.DeduplicateReportNormIDs(model);
                //Check công thức
                Log.Information("[Formula] Start calculating the {Count} formula for {Stock}...", formulas.Count, model.Meta?.MaCongTy ?? "UNKNOWN");

                //Lấy ID theo SheetCode, NormName
                var allReportNorms = (await _financialService.GetAllReportNormsDB()).ToList();

                //Danh sách quá khứ
                var reportData_DatabaseQK = await _financialService.GetReportDataFull(model.Meta.MaCongTy, model.Meta.KyBaoCao, (model.Meta.Nam - 1), model.Meta.TinhChatBaoCao, model.Meta.TrangThaiKiemDuyet, 1);
                var dbDetailsListQK = reportData_DatabaseQK?.ToList() ?? new List<ReportDataDetailItem>();

                var executor = new FormulaExecutor();
                var remap = new FormulaAutoRemap();
                
                try
                {
                    const int MaxRemapAttempts = 3;
                    int remapAttempt = 0;
                    ResultCheckFormula formula;
                    HashSet<string> previousErrors = new HashSet<string>();

                    do
                    {
                        remapAttempt++;
                        Log.Information("[FORMULA] Formula check #{Attempt}", remapAttempt);

                        // Chạy công thức
                        formula = executor.Execute(formulas, model, dbDetailsListQK, model.Meta?.MaCongTy ?? "UNKNOWN", remapAttempt);

                        if (formula.FormulaErrors.Count == 0)
                        {
                            Log.Information("[FORMULA] All formulas passed at this time #{Attempt}!", remapAttempt);
                            break;
                        }

                        // Tạo key duy nhất cho mỗi lỗi: "SheetCode|ParentID"
                        var currentErrors = new HashSet<string>(formula.FormulaErrors.Select(e => $"{e.SheetCode}|{e.Parent.ID}"));

                        // So sánh với lỗi lần trước
                        if (remapAttempt > 1)
                        {
                            // Kiểm tra xem có lỗi mới không
                            var newErrors = currentErrors.Except(previousErrors).ToList();
                            var fixedErrors = previousErrors.Except(currentErrors).ToList();

                            if (newErrors.Count == 0 && fixedErrors.Count == 0)
                            {
                                Log.Warning("[FORMULA] The error remains the same as before, STOP remapping.");
                                Log.Warning("[FORMULA] There are still {Count} errors that cannot be fixed:", currentErrors.Count);

                                foreach (var err in formula.FormulaErrors)
                                {
                                    Log.Warning("  - [{Sheet}] ParentID={PID}, Formula={F}", err.SheetCode, err.Parent.ID, err.Formula);
                                }
                                break;
                            }

                            if (newErrors.Count == 0 && fixedErrors.Count > 0)
                            {
                                Log.Warning("[FORMULA] There are still {Count} errors that cannot be fixed:", currentErrors.Count);
                                Log.Information("[FORMULA] The {Count} error has been fixed:", fixedErrors.Count);
                                foreach (var errKey in fixedErrors.Take(5))
                                {
                                    Log.Information("  + {Err}", errKey);
                                }
                                break;
                            }

                            if (newErrors.Count > 0)
                            {
                                Log.Warning("[FORMULA] A new {Count} error has appeared:", newErrors.Count);
                                foreach (var errKey in newErrors.Take(5))
                                {
                                    Log.Warning("  - {Err}", errKey);
                                }
                            }
                        }

                        // Còn lỗi và chưa quá số lần retry -> Remap
                        if (remapAttempt < MaxRemapAttempts)
                        {
                            var remapCandidates = formula.FormulaErrors.Where(x => x.Differences == null).ToList();
                            var realFormulaErrors = formula.FormulaErrors.Where(x => x.Differences != null).ToList();

                            Log.Information("[FORMULA CHECK] Total Errors={Total} | Actual Formula Errors={RealErrorCount} | Possible Mapping Issues={RemapCount} | Attempt #{Attempt}",
                                formula.FormulaErrors.Count, realFormulaErrors.Count, remapCandidates.Count, remapAttempt);

                            var candidate = remap.Process(formula, model);
                            if (candidate == null || !candidate.Any() || !candidate.Any(x => x.MatchedCombinations != null && x.MatchedCombinations.Any()))
                            {
                                Log.Warning("[REMAP] No valid remap candidates were found. Remapping process stopped.");
                                break;
                            }
                            //candidate = CandidateFilter.Candidate(candidate, model);
                            var remapResult = await _financialReMappingService.ReMapFinancialIndicatorsAsync(candidate, allReportNorms);

                            if (remapResult?.Groups == null || remapResult.Groups.Count == 0)
                            {
                                Log.Warning("[REMAP] No remap results, stopping.");
                                break;
                            }
                            Log.Information("[Remap] Received {Count} group(s) remapping results, apply...", remapResult.Groups.Count);


                            // ===== LỌC KẾT QUẢ AI - KIỂM TRA QUAN HỆ CON VỚI CÁC CÔNG THỨC SAI KHÁC =====
                            var (conflictUpdate, _) = RemapApplyHelper.FilterParentChildConflict(remapResult, candidate, formula, model);

                            bool hasAnyUpdate = conflictUpdate;

                            // ===== CẬP NHẬT MODEL =====
                            hasAnyUpdate = RemapApplyHelper.ApplyRemapResult(remapResult, model, formulas);

                            if (!hasAnyUpdate)
                            {
                                Log.Warning("[REMAP] No changes have been applied, stop.");
                                break;
                            }

                            // Lưu lỗi hiện tại để so sánh lần sau
                            previousErrors = currentErrors;

                            Log.Information("[REMAP] Update complete, preparing to run the formula again...");
                            await Task.Delay(1000, ct);
                        }
                        else
                        {
                            Log.Warning("[FORMULA] I've tried remapping {Max} several times, but there's still a formula error in {Count}:", MaxRemapAttempts, currentErrors.Count);

                            foreach (var err in formula.FormulaErrors.Take(10))
                            {
                                Log.Warning("[FORMULA ERROR] [{Sheet}] ParentID={PID}, Formula={F}", err.SheetCode, err.Parent.ID, err.Formula);
                            }
                            break;
                        }

                    } while (remapAttempt < MaxRemapAttempts);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Formula] Fatal Error during the calculation process");
                }

                try
                {
                    Dictionary<int, decimal?> values = new();
                    var rows = (model.BalanceSheet ?? Enumerable.Empty<FinancialReportItem>())
                                .Concat(model.IncomeStatement ?? Enumerable.Empty<FinancialReportItem>())
                                .Concat(model.CashFlow ?? Enumerable.Empty<FinancialReportItem>())
                                .Concat(model.OffBalanceSheet ?? Enumerable.Empty<FinancialReportItem>());

                    foreach (var row in rows)
                    {
                        if (!row.ReportNormID.HasValue || row.Values == null || row.Values.Count == 0)
                        {
                            continue;
                        }

                        values[row.ReportNormID.Value] = row.Values.Values.FirstOrDefault();
                    }

                    bool isCashFlowIndirect = string.Equals(model.CashFlowMethod, "indirect", StringComparison.OrdinalIgnoreCase);
                    var sheets = new Dictionary<string, List<FinancialReportItem>>
                    {
                        ["CD"] = model.BalanceSheet,
                        ["KQ"] = model.IncomeStatement,
                        ["NB"] = model.OffBalanceSheet,
                        ["LCGT"] = isCashFlowIndirect ? model.CashFlow : null,
                        ["LCTT"] = !isCashFlowIndirect ? model.CashFlow : null
                    };

                    foreach (var sheet in sheets.Values)
                    {
                        if (sheet == null || sheet.Count == 0)
                            continue;

                        bool hasCol0Data = sheet.Any(x => x?.Values?.Values != null && (x.Values.Values.ElementAtOrDefault(0) ?? 0) != 0);

                        int columnIndex = hasCol0Data ? 0 : 2;

                        foreach (var row in sheet)
                        {
                            if (!row.ReportNormID.HasValue || row.Values?.Values == null)
                                continue;

                            var value = row.Values.Values.ElementAtOrDefault(columnIndex);

                            values[row.ReportNormID.Value] = value ?? 0;
                        }
                    }

                    //Danh sách hiện tại
                    var reportData_Database = await _financialService.GetReportDataFull(model.Meta.MaCongTy, model.Meta.KyBaoCao, model.Meta.Nam, model.Meta.TinhChatBaoCao, model.Meta.TrangThaiKiemDuyet, 0);
                    var dbDetailsRaw = reportData_Database?.ToList() ?? new List<ReportDataDetailItem>();

                    var dbDetails = new List<ReportDataDetailItem>();

                    foreach (var group in dbDetailsRaw.GroupBy(x => x.ReportNormID))
                    {
                        var has0 = group.Any(x => x.IsCumulative == 0);
                        var has1 = group.Any(x => x.IsCumulative == 1);

                        if (has0 && has1)
                            dbDetails.AddRange(group.Where(x => x.IsCumulative == 0));
                        else
                            dbDetails.AddRange(group);
                    }
                    var dbReportNormIds = dbDetails.Select(x => x.ReportNormID).ToHashSet();

                    Log.Information("--- BẮT ĐẦU ĐỐI CHIẾU DỮ LIỆU (AI vs DB) ---");

                    foreach (var dbItem in dbDetails)
                    {
                        if (!values.TryGetValue(dbItem.ReportNormID, out var mapValue) || mapValue == null)
                        {
                            Log.Warning("[DB có - MAP không] NormID={ID}, DB={Val}", dbItem.ReportNormID, dbItem.Value);
                            continue;
                        }

                        if (mapValue != dbItem.Value)
                        {
                            Log.Warning("[LỆCH GIÁ TRỊ] NormID={ID} | Map={MapVal} <> DB={DbVal} | Diff={Diff}", dbItem.ReportNormID, mapValue, dbItem.Value, (mapValue - dbItem.Value));
                        }
                    }

                    foreach (var mapItem in values)
                    {
                        if (!dbReportNormIds.Contains(mapItem.Key) && mapItem.Value.HasValue && mapItem.Value.Value != 0 && mapItem.Key > 2000)
                        {
                            Log.Information("[MAP có - DB không] NormID={ID}, Map={Val}", mapItem.Key, mapItem.Value);
                        }
                    }

                    Log.Information("--- KẾT THÚC ĐỐI CHIẾU ---");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Validation] Lỗi khi đối chiếu dữ liệu với DB");
                }

                //update lại giá trị chỉ tiêu
                void ApplyMappingToSetReportNormID(List<FinancialReportItem> rows, IEnumerable<dynamic> mappings)
                {
                    foreach (var m in mappings)
                    {
                        if (m.ScanIndex == null)
                            continue;

                        int idx = (int)m.ScanIndex;
                        int? normId = m.ReportNormID != null ? (int?)m.ReportNormID : null;

                        if (idx <= 0 || idx > rows.Count)
                            continue;

                        rows[idx - 1].ReportNormID = normId;
                    }
                }

                scanned.BalanceSheet = CovertModel(model.BalanceSheet);
                scanned.IncomeStatement = CovertModel(model.IncomeStatement);
                scanned.CashFlow = CovertModel(model.CashFlow);
                scanned.OffBalanceSheet = CovertModel(model?.OffBalanceSheet);

                try
                {
                    var jsonDebug = JsonSerializer.Serialize(scanned, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    using var doc = JsonDocument.Parse(jsonDebug);

                    string debugExcelPath = ExcelExporter.Export(doc.RootElement, baseName, new List<NormRow>(), scanned);

                    if (!string.IsNullOrEmpty(debugExcelPath))
                    {
                        Log.Information($"[DEBUG] Đã xuất file Excel nóng hổi tại: {debugExcelPath}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[DEBUG] Lỗi xuất Excel: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[AutoProcessor::TryMapWithRetryAsync] Error: " + ex.ToString());
                return false;
            }
        }
        private List<Row> CovertModel(List<FinancialReportItem> financialReportItem)
        {
            List<Row> rows = new List<Row>();
            foreach (var item in financialReportItem)
            {
                rows.Add(new Row
                {
                    Item = item.Item,
                    ReportNormID = item.ReportNormID.ToString(),
                    Code = item.Code,
                    PublishNormCode = item.PublishNormCode,
                    NormName = item.NormName,
                    Values = item.Values,
                    ParentName = item.ParentName
                });
            }
            return rows;
        }
        private List<FinancialReportItem> MapRowsToItems(List<Row> rows)
        {
            if (rows == null || rows.Count == 0) return new List<FinancialReportItem>();

            return rows.Select((row, index) =>
            {
                int? normId = null;
                if (int.TryParse(row.ReportNormID, out int parsedId))
                {
                    normId = parsedId;
                }
                var valuesDecimal = new Dictionary<string, decimal?>();
                if (row.Values != null)
                {
                    foreach (var kvp in row.Values)
                    {
                        valuesDecimal[kvp.Key] = kvp.Value.HasValue ? (decimal?)kvp.Value.Value : null;
                    }
                }

                return new FinancialReportItem
                {
                    ScanIndex = index,
                    Item = row.Item,
                    Values = valuesDecimal,
                    ReportNormID = normId,
                    Code = row.Code,
                    NormName = row.NormName,
                    PublishNormCode = row.PublishNormCode,
                    ParentName = row.ParentName
                };
            }).ToList();
        }

        private async Task<int> ApplyValidatedCacheToItems(List<FinancialReportItem> items, string stockCode, int bizId, int year, string term, ReportComponentType comp)
        {
            if (items == null) return 0;
            int hits = 0;
            foreach (var item in items)
            {
                var cached = await _fileMappingCache.GetValidatedMappingAsync(stockCode, bizId, year, term, (int)comp, item.Item, item.ParentName, item.Code);
                if (cached != null)
                {
                    item.ReportNormID = cached.ReportNormID;
                    item.NormName = cached.NormName;
                    item.PublishNormCode = cached.PublishNormCode;
                    hits++;
                }
            }
            return hits;
        }

        private async Task ValidateAndSaveToCacheAsync(string stock, int bizId, string term, int year, List<FinancialReportItem> items, MappingResponse mapRes, ReportComponentType comp)
        {
            if (mapRes?.Mappings == null || items == null) return;
            var toCache = new List<ValidatedMappingCache>();
            foreach (var m in mapRes.Mappings)
            {
                if (!m.ReportNormID.HasValue) continue;
                var item = items.FirstOrDefault(x => x.ScanIndex == m.ScanIndex - 1);
                if (item == null) continue;
                var (dbVal, margin, isValid) = await ValidateMappingWithDbAsync(stock, term, year, m.ReportNormID.Value, item.Values?.Values.FirstOrDefault());
                toCache.Add(new ValidatedMappingCache
                {
                    StockCode = stock,
                    BusinessTypeID = bizId,
                    ComponentType = (int)comp,
                    ItemName = item.Item,
                    ParentName = item.ParentName,
                    Code = item.Code,
                    ReportNormID = m.ReportNormID.Value,
                    NormName = item.NormName,
                    PublishNormCode = item.PublishNormCode,
                    ValidationMethod = "AI",
                    Status = isValid ? ValidationStatus.Validated : ValidationStatus.Suspicious,
                    ConfidenceScore = isValid ? 95 : 50,
                    ComparisonValue = item.Values?.Values.FirstOrDefault(),
                    DbValue = dbVal,
                    ErrorMarginPct = margin
                });
            }
            if (toCache.Any()) await _fileMappingCache.SaveBatchValidatedMappingsAsync(toCache, year, term);
        }

        private async Task<(decimal? dbValue, decimal? errorMargin, bool isValid)> ValidateMappingWithDbAsync(string stock, string term, int year, int normId, decimal? val)
        {
            if (!val.HasValue || val == 0) return (null, null, false);
            try
            {
                var (_, dbDetails) = await _financialService.GetReportData(stock, term, year, "LC", "G", "CKT");
                var dbItem = dbDetails?.FirstOrDefault(x => x.ReportNormID == normId);
                if (dbItem?.Value == null || dbItem.Value == 0) return (null, null, false);
                decimal margin = Math.Abs((val.Value - dbItem.Value.Value) / dbItem.Value.Value * 100);
                return (dbItem.Value, margin, margin <= 5m);
            }
            catch { return (null, null, false); }
        }
    }
}