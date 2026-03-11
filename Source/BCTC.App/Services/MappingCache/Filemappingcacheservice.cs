using BCTC.App.IService;
using BCTC.BusinessLogic.OcrLogic;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BCTC.App.Services.MappingCache
{
    /// <summary>
    /// Service quản lý cache file JSON trong wwwroot/mapping_cache/
    /// Cấu trúc: wwwroot/mapping_cache/{Year}/{BusinessType}/{Term}/{StockCode}.json
    /// </summary>
    public class FileMappingCacheService : IFileMappingCacheService
    {
        private readonly ILogger<FileMappingCacheService> _logger;
        private readonly string _cacheDirectory;
        private readonly TimeSpan _cacheExpiration;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        private readonly ConcurrentDictionary<string, CompanyCacheContainer> _memoryIndex
            = new ConcurrentDictionary<string, CompanyCacheContainer>();

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true
        };

        public FileMappingCacheService(
            ILogger<FileMappingCacheService> logger,
            TimeSpan? cacheExpiration = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            string wwwRootPath = PathHelper.GetWwwRoot();

            _cacheDirectory = Path.Combine(wwwRootPath, "mapping_cache");
            _cacheExpiration = cacheExpiration ?? TimeSpan.FromDays(180);

            // Tạo thư mục gốc nếu chưa có
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }

            _logger.LogInformation("[FileMappingCache] Initialized at project root: {Path}", _cacheDirectory);

            // Load existing cache vào memory
            _ = LoadAllCacheAsync();
        }

        /// <summary>
        /// Lấy cache mapping (đã validate) kèm logic Fallback tìm kiếm lùi
        /// </summary>
        public async Task<ValidatedMappingCache> GetValidatedMappingAsync(
            string stockCode,
            int businessTypeId,
            int year,
            string term,
            int componentType,
            string itemName,
            string parentName,
            string code = null)
        {
            try
            {
                // 1. Tìm đúng kỳ hiện tại
                var cached = await TryGetFromSpecificFolder(stockCode, businessTypeId, year, term, componentType, itemName, parentName, code);
                if (cached != null) return cached;

                // 2. Nếu không có, tìm trong Báo cáo Năm (N) của năm đó (thường đầy đủ nhất)
                if (!string.Equals(term, "N", StringComparison.OrdinalIgnoreCase))
                {
                    cached = await TryGetFromSpecificFolder(stockCode, businessTypeId, year, "N", componentType, itemName, parentName, code);
                    if (cached != null)
                    {
                        _logger.LogDebug("[CACHE FALLBACK] Hit Year={Year} Term=N for {Stock}", year, stockCode);
                        return cached;
                    }
                }

                // 3. Nếu vẫn không có, tìm trong Báo cáo Năm (N) của năm trước đó
                cached = await TryGetFromSpecificFolder(stockCode, businessTypeId, year - 1, "N", componentType, itemName, parentName, code);
                if (cached != null)
                {
                    _logger.LogDebug("[CACHE FALLBACK] Hit Year={Year} Term=N for {Stock}", year - 1, stockCode);
                }

                return cached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CACHE GET] Error: {Stock}-{Item}", stockCode, itemName);
                return null;
            }
        }

        /// <summary>
        /// Hàm nội bộ thực hiện tìm kiếm trong 1 folder cụ thể
        /// </summary>
        private async Task<ValidatedMappingCache> TryGetFromSpecificFolder(
            string stockCode, int businessTypeId, int year, string term,
            int componentType, string itemName, string parentName, string code)
        {
            var container = await GetCompanyCacheAsync(stockCode, businessTypeId, year, term);
            if (container == null) return null;

            var primaryKey = MappingCacheKeyBuilder.BuildPrimary(stockCode, businessTypeId, componentType, itemName, parentName, code);
            var item = container.Items.FirstOrDefault(x =>
                MappingCacheKeyBuilder.BuildPrimary(x.StockCode, x.BusinessTypeID, x.ComponentType, x.ItemName, x.ParentName, x.Code) == primaryKey);

            if (item != null && IsValidCache(item))
            {
                await UpdateUsageStatsAsync(stockCode, businessTypeId, year, term, item);
                return item;
            }

            // Fallback theo tên nếu có mã code đầu vào
            if (!string.IsNullOrEmpty(code))
            {
                var secondaryKey = MappingCacheKeyBuilder.BuildSecondary(stockCode, businessTypeId, componentType, itemName, parentName);
                item = container.Items.FirstOrDefault(x =>
                    MappingCacheKeyBuilder.BuildSecondary(x.StockCode, x.BusinessTypeID, x.ComponentType, x.ItemName, x.ParentName) == secondaryKey);
            }

            return (item != null && IsValidCache(item)) ? item : null;
        }

        /// <summary>
        /// Lưu cache mapping lẻ
        /// </summary>
        public async Task SaveValidatedMappingAsync(ValidatedMappingCache cache)
        {
            // Mặc định lưu vào kỳ hiện tại nếu không rõ thông tin năm/kỳ
            await SaveBatchValidatedMappingsAsync(new List<ValidatedMappingCache> { cache }, DateTime.Now.Year, "UNK");
        }

        /// <summary>
        /// Lưu nhiều cache cùng lúc (batch) theo Năm và Kỳ
        /// </summary>
        public async Task SaveBatchValidatedMappingsAsync(List<ValidatedMappingCache> caches, int year, string term)
        {
            if (caches == null || caches.Count == 0) return;

            try
            {
                await _fileLock.WaitAsync();
                var groups = caches.GroupBy(x => new { x.StockCode, x.BusinessTypeID });

                foreach (var group in groups)
                {
                    var container = await GetCompanyCacheAsync(group.Key.StockCode, group.Key.BusinessTypeID, year, term)
                        ?? new CompanyCacheContainer
                        {
                            StockCode = group.Key.StockCode,
                            BusinessTypeID = group.Key.BusinessTypeID,
                            Items = new List<ValidatedMappingCache>()
                        };

                    foreach (var cache in group)
                    {
                        if (cache.Status == ValidationStatus.Rejected) continue;

                        var primaryKey = MappingCacheKeyBuilder.BuildPrimary(cache.StockCode, cache.BusinessTypeID, cache.ComponentType, cache.ItemName, cache.ParentName, cache.Code);
                        var existing = container.Items.FirstOrDefault(x =>
                            MappingCacheKeyBuilder.BuildPrimary(x.StockCode, x.BusinessTypeID, x.ComponentType, x.ItemName, x.ParentName, x.Code) == primaryKey);

                        if (existing != null)
                        {
                            existing.ReportNormID = cache.ReportNormID;
                            existing.NormName = cache.NormName;
                            existing.PublishNormCode = cache.PublishNormCode;
                            existing.Status = cache.Status;
                            existing.ValidationMethod = cache.ValidationMethod;
                            existing.ConfidenceScore = cache.ConfidenceScore;
                            existing.ComparisonValue = cache.ComparisonValue;
                            existing.DbValue = cache.DbValue;
                            existing.ErrorMarginPct = cache.ErrorMarginPct;
                            existing.LastUsedAt = DateTime.UtcNow;
                            existing.LastValidatedAt = DateTime.UtcNow;
                            existing.UsageCount++;
                            existing.ExpiresAt = DateTime.UtcNow.Add(_cacheExpiration);
                            existing.Notes = cache.Notes;
                        }
                        else
                        {
                            cache.CreatedAt = DateTime.UtcNow;
                            cache.LastUsedAt = DateTime.UtcNow;
                            cache.LastValidatedAt = DateTime.UtcNow;
                            cache.UsageCount = 1;
                            cache.ExpiresAt = DateTime.UtcNow.Add(_cacheExpiration);
                            container.Items.Add(cache);
                        }
                    }

                    container.LastUpdated = DateTime.UtcNow;
                    container.TotalItems = container.Items.Count;

                    await SaveContainerToFileAsync(container, year, term);
                    _memoryIndex[GetIndexKey(group.Key.StockCode, group.Key.BusinessTypeID, year, term)] = container;
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "[CACHE BATCH SAVE] Error"); }
            finally { _fileLock.Release(); }
        }

        public async Task ClearCompanyCacheAsync(string stockCode, int businessTypeId)
        {
            try
            {
                await _fileLock.WaitAsync();
                var pattern = $"{stockCode.ToUpperInvariant()}.json";
                var files = Directory.GetFiles(_cacheDirectory, pattern, SearchOption.AllDirectories);
                foreach (var file in files) { File.Delete(file); }

                var keysToRemove = _memoryIndex.Keys.Where(k => k.Contains(stockCode.ToUpperInvariant())).ToList();
                foreach (var key in keysToRemove) { _memoryIndex.TryRemove(key, out _); }
            }
            finally { _fileLock.Release(); }
        }

        public async Task CleanExpiredCacheAsync()
        {
            try
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var json = await File.ReadAllTextAsync(file);
                    var container = JsonSerializer.Deserialize<CompanyCacheContainer>(json, _jsonOptions);
                    if (container == null) continue;
                    var originalCount = container.Items.Count;
                    container.Items = container.Items.Where(x => !x.ExpiresAt.HasValue || x.ExpiresAt.Value > DateTime.UtcNow).ToList();
                    if (container.Items.Count != originalCount)
                    {
                        if (container.Items.Count == 0) File.Delete(file);
                        else await File.WriteAllTextAsync(file, JsonSerializer.Serialize(container, _jsonOptions));
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "[CACHE CLEANUP] Fatal error"); }
        }

        public async Task<Dictionary<string, object>> GetCacheStatisticsAsync(string stockCode = null)
        {
            var stats = new Dictionary<string, object>();
            var files = Directory.GetFiles(_cacheDirectory, "*.json", SearchOption.AllDirectories);
            stats["TotalFiles"] = files.Length;
            stats["CacheDirectory"] = _cacheDirectory;
            return stats;
        }

        // ============================================================
        // PRIVATE HELPERS
        // ============================================================

        private string GetCacheFilePath(string stockCode, int businessTypeId, int year, string term)
        {
            string folderYear = year.ToString();
            string bizKey = businessTypeId switch { 1 => "CP", 2 => "CK", 3 => "NH", 5 => "BH", _ => "OTHER" };
            string folderTerm = string.IsNullOrWhiteSpace(term) ? "UNK" : term.ToUpperInvariant();

            // Cấu trúc folder: mapping_cache/2024/CP/Q1/
            string fullFolderPath = Path.Combine(_cacheDirectory, folderYear, bizKey, folderTerm);

            if (!Directory.Exists(fullFolderPath))
                Directory.CreateDirectory(fullFolderPath);

            return Path.Combine(fullFolderPath, $"{stockCode.ToUpperInvariant()}.json");
        }

        private string GetIndexKey(string stockCode, int businessTypeId, int year, string term)
        {
            string folderTerm = string.IsNullOrWhiteSpace(term) ? "UNK" : term.ToUpperInvariant();
            return $"{year}_{businessTypeId}_{folderTerm}_{stockCode.ToUpperInvariant()}";
        }

        private async Task<CompanyCacheContainer> GetCompanyCacheAsync(string stockCode, int businessTypeId, int year, string term)
        {
            var indexKey = GetIndexKey(stockCode, businessTypeId, year, term);
            if (_memoryIndex.TryGetValue(indexKey, out var cached)) return cached;

            var filePath = GetCacheFilePath(stockCode, businessTypeId, year, term);
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var container = JsonSerializer.Deserialize<CompanyCacheContainer>(json, _jsonOptions);
                if (container != null) _memoryIndex[indexKey] = container;
                return container;
            }
            catch { return null; }
        }

        private async Task SaveContainerToFileAsync(CompanyCacheContainer container, int year, string term)
        {
            var filePath = GetCacheFilePath(container.StockCode, container.BusinessTypeID, year, term);
            var json = JsonSerializer.Serialize(container, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        private async Task LoadAllCacheAsync()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory)) return;
                var files = Directory.GetFiles(_cacheDirectory, "*.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var container = JsonSerializer.Deserialize<CompanyCacheContainer>(json, _jsonOptions);
                        if (container != null)
                        {
                            var parts = file.Replace(_cacheDirectory, "").Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                int.TryParse(parts[0], out int year);
                                string term = parts[2];
                                var indexKey = GetIndexKey(container.StockCode, container.BusinessTypeID, year, term);
                                _memoryIndex[indexKey] = container;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private bool IsValidCache(ValidatedMappingCache cache)
        {
            if (cache.Status != ValidationStatus.Validated && cache.Status != ValidationStatus.ManualApproved) return false;
            if (cache.ExpiresAt.HasValue && cache.ExpiresAt.Value <= DateTime.UtcNow) return false;
            return true;
        }

        private async Task UpdateUsageStatsAsync(string stockCode, int businessTypeId, int year, string term, ValidatedMappingCache cache)
        {
            try
            {
                cache.LastUsedAt = DateTime.UtcNow;
                cache.UsageCount++;

                _ = Task.Run(async () =>
                {
                    await _fileLock.WaitAsync();
                    try
                    {
                        var container = await GetCompanyCacheAsync(stockCode, businessTypeId, year, term);
                        if (container != null) await SaveContainerToFileAsync(container, year, term);
                    }
                    finally { _fileLock.Release(); }
                });
            }
            catch { }
        }
    }
}