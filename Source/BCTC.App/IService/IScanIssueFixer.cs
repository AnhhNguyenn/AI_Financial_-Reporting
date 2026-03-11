using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Report;

namespace BCTC.App.IService
{
    public interface IScanIssueFixer
    {
        Task FixAsync(
            ExtractResult data,
            string pdfPath,
            CompanyReportDto report,
            CancellationToken ct);
    }
}
