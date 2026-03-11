using BCTC.App.Services.MappingCache;

namespace BCTC.App.IService
{
    public interface IFileMappingCacheService
    {
        /// <summary>
        /// Lấy cache mapping đã validate (Cần year và term để tìm đúng folder)
        /// </summary>
        Task<ValidatedMappingCache> GetValidatedMappingAsync(
            string stockCode,
            int businessTypeId,
            int year,
            string term,
            int componentType,
            string itemName,
            string parentName,
            string code = null);

        /// <summary>
        /// Lưu cache mapping (sau khi validate)
        /// </summary>
        Task SaveValidatedMappingAsync(ValidatedMappingCache cache);

        /// <summary>
        /// Lưu nhiều cache cùng lúc theo Năm và Kỳ
        /// </summary>
        Task SaveBatchValidatedMappingsAsync(List<ValidatedMappingCache> caches, int year, string term);

        /// <summary>
        /// Xóa cache của 1 công ty
        /// </summary>
        Task ClearCompanyCacheAsync(string stockCode, int businessTypeId);

        /// <summary>
        /// Dọn dẹp cache expired
        /// </summary>
        Task CleanExpiredCacheAsync();

        /// <summary>
        /// Lấy thống kê cache
        /// </summary>
        Task<Dictionary<string, object>> GetCacheStatisticsAsync(string stockCode = null);
    }
}