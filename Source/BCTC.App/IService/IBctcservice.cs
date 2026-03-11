using BCTC.DataAccess.Models;

namespace BCTC.App.IService
{
    public interface IBctcservice
    {
        Task<(ExtractResult data, string modelUsed, UsageInfo usage)> ExtractAsync(
            string name, byte[] pdfBytes, string mime, string? normPath,
            string? modelOverride, bool refineWithGemini, int? businessTypeId, CancellationToken ct);

        Task<UsageInfo> MapAsync(ExtractResult result, string normFolder, CancellationToken ct);
        List<ExtractResult> SplitCashFlowResult(ExtractResult original);
        Task DeleteFileAsync(string fileUri);
    }
}
