using Microsoft.Extensions.Configuration;

namespace BCTC.BusinessLogic.OcrLogic
{
    public static class PathHelper
    {
        private static IConfiguration? _config;

        public static void Init(IConfiguration config)
        {
            _config = config;
        }

        public static string GetWwwRoot()
        {
            var dir = Directory.GetCurrentDirectory();

            if (dir.Contains("bin"))
            {
                dir = Directory.GetParent(dir)!.Parent!.Parent!.FullName;
            }

            if (!dir.EndsWith("BCTC.App"))
            {
                var temp = new DirectoryInfo(dir);
                for (int i = 0; i < 5 && temp != null; i++)
                {
                    if (temp.GetDirectories("BCTC.App").Any())
                    {
                        dir = Path.Combine(temp.FullName, "BCTC.App");
                        break;
                    }
                    temp = temp.Parent;
                }
            }

            var wwwroot = Path.Combine(dir, "wwwroot");
            Directory.CreateDirectory(wwwroot);

            return wwwroot;
        }

        public static string PdfDownload(string year, string term, string bizFolder, string fileName)
            => Build("PdfDownload", year, term, bizFolder, fileName);

        public static string PdfChunk(string year, string term, string bizFolder, string fileName)
            => Build("PdfChunk", year, term, bizFolder, fileName);

        public static string JsonScan(string year, string term, string bizFolder, string fileName)
            => Build("JsonScan", year, term, bizFolder, fileName);

        public static string JsonMap(string year, string term, string bizFolder, string fileName)
            => Build("JsonMap", year, term, bizFolder, fileName);

        private static string Build(string key, string year, string term, string bizFolder, string fileName)
        {
            if (_config == null) return Path.Combine(GetWwwRoot(), key, fileName);
            string folder = _config.GetSection("Path")[key] ?? key;
            var structure = _config.GetSection("Path:Structure:Order").Get<string[]>()
                            ?? new[] { "year", "month", "term" };
            bool useYear = _config.GetValue<bool?>("Path:Structure:UseYear") ?? true;
            bool useMonth = _config.GetValue<bool?>("Path:Structure:UseMonth") ?? true;
            bool useTerm = _config.GetValue<bool?>("Path:Structure:UseTerm") ?? true;
            year = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString() : year;
            string month = DateTime.Now.ToString("MM");
            term = string.IsNullOrWhiteSpace(term) ? "UNK" : term;
            var parts = new List<string>();
            foreach (var item in structure)
            {
                if (item == "year" && useYear) parts.Add(year);
                if (item == "month" && useMonth) parts.Add(month);
                if (item == "term" && useTerm) parts.Add(term);
            }
            if (!string.IsNullOrWhiteSpace(bizFolder)) parts.Add(bizFolder);

            string baseDir = GetWwwRoot();
            string targetDir = Path.Combine(new[] { baseDir, folder }.Concat(parts).ToArray());

            Directory.CreateDirectory(targetDir);
            return Path.Combine(targetDir, fileName);
        }

        public static string Excel(string businessType, string year, string term, string fileName)
        {
            if (_config == null) return Path.Combine(GetWwwRoot(), "Excel", fileName);

            businessType = string.IsNullOrWhiteSpace(businessType) ? "other" : businessType.ToLower();

            string folderKey = "Excel";
            string baseFolder = _config.GetSection("Path")[folderKey] ?? folderKey;

            var structure = _config.GetSection("Path:Structure:Order").Get<string[]>()
                            ?? new[] { "year", "month", "term" };

            bool useYear = _config.GetValue<bool?>("Path:Structure:UseYear") ?? true;
            bool useMonth = _config.GetValue<bool?>("Path:Structure:UseMonth") ?? true;
            bool useTerm = _config.GetValue<bool?>("Path:Structure:UseTerm") ?? true;

            year = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString() : year;
            string month = DateTime.Now.ToString("MM");
            term = string.IsNullOrWhiteSpace(term) ? "UNK" : term;

            var parts = new List<string> { businessType };

            foreach (var item in structure)
            {
                if (item == "year" && useYear) parts.Add(year);
                if (item == "month" && useMonth) parts.Add(month);
                if (item == "term" && useTerm) parts.Add(term);
            }

            string baseDir = GetWwwRoot();
            string targetDir = Path.Combine(new[] { baseDir, baseFolder }.Concat(parts).ToArray());

            Directory.CreateDirectory(targetDir);
            return Path.Combine(targetDir, fileName);
        }
    }
}