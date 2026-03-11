using BCTC.DataAccess.Models.Norm;
using Serilog;
using System.Xml.Linq;

namespace BCTC.BusinessLogic.NormLogic
{
    public class NormMatcher
    {
        public static List<NormRow> LoadNorms(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    Log.Warning("[NORM][WARN] Đường dẫn XML rỗng, không thể load.");
                    return new List<NormRow>();
                }

                if (!File.Exists(path))
                {
                    Log.Warning("[NORM][MISS] Không tìm thấy file norm: {Path}", path);
                    return new List<NormRow>();
                }

                var xdoc = XDocument.Load(path);
                var rows = xdoc.Descendants("rn")
                    .Select(x => new NormRow
                    {
                        ReportNormID = (string?)x.Element("ReportNormID"),
                        Code = (string?)x.Element("Code"),
                        Name = (string?)x.Element("Name"),
                        PublishNormCode = (string?)x.Element("PublishNormCode"),
                        ParentName = (string?)x.Element("ParentName")
                    })
                    .Where(r => !string.IsNullOrEmpty(r.ReportNormID))
                    .ToList();

                Log.Information("[NORM][LOAD] Đã load {Count} dòng norm từ {Path}", rows.Count, path);
                return rows;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[NORM][FAIL] Lỗi khi load file norm: {Path}", path);
                return new List<NormRow>();
            }
        }
    }
}
