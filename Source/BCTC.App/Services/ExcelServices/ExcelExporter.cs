using BCTC.BusinessLogic.OcrLogic;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Norm;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BCTC.App.Services.ExcelServices
{
    public static class ExcelExporter
    {
        private static readonly Dictionary<int, string> TemplateMap = new()
        {
            { 1, "CP.xls" },
            { 2, "CK.xls" },
            { 3, "NH.xls" },
            { 5, "BH.xls" },
        };

        private static readonly Dictionary<int, string> FolderMap = new()
        {
            { 1, "CP" },
            { 2, "CK" },
            { 3, "NH" },
            { 5, "BH" },
        };

        public static string Export(JsonElement root, string companyOrPath, List<NormRow> norms, ExtractResult result, int colIndex = 3)
        {
            string key = result.Meta?.MaCongTy ?? result.Company ?? companyOrPath ?? "UNKNOWN";
            int businessTypeId = (int)result.BusinessTypeID;

            try
            {
                if (!TemplateMap.TryGetValue(businessTypeId, out var templateName))
                    templateName = "CP.xls";

                string businessFolder = FolderMap.TryGetValue(businessTypeId, out var folder) ? folder : "CP";
                string templatePath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "Templates", templateName);

                var meta = result.Meta ?? new Meta();
                var metaDB = result.MetaDB ?? new MetaDB();

                string maCK = meta.MaCongTy ?? result.Company ?? "UNKNOWN";
                string kyBC = meta.KyBaoCao?.Trim().ToUpperInvariant() ?? "UNK";
                string namBC = meta.Nam.ToString() ?? DateTime.Now.Year.ToString();

                string exportPath;

                if (companyOrPath.Contains(Path.DirectorySeparatorChar) || companyOrPath.Contains(Path.AltDirectorySeparatorChar))
                {
                    exportPath = companyOrPath;

                    string dir = Path.GetDirectoryName(exportPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                else
                {
                    string safeTerm = Regex.Replace(kyBC, @"\s+", "_").ToUpperInvariant();
                    string safeYear = Regex.Replace(namBC, @"\D", "");

                    string exportName;
                    if (!string.IsNullOrWhiteSpace(companyOrPath))
                    {
                        exportName = companyOrPath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
                            ? companyOrPath
                            : $"{companyOrPath}.xls";
                    }
                    else
                    {
                        string safeCompany = MakeSafeName(maCK);
                        exportName = $"{safeCompany}_{safeTerm}_{safeYear}.xls";
                    }

                    string businessType = businessFolder.ToLower();
                    exportPath = PathHelper.Excel(businessType, safeYear, safeTerm, exportName);

                    int counter = 1;
                    while (File.Exists(exportPath))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(exportName);
                        if (Regex.IsMatch(nameWithoutExt, @"_Excel_\d+$"))
                        {
                            nameWithoutExt = Regex.Replace(nameWithoutExt, @"_Excel_\d+$", "");
                        }

                        string newName = $"{nameWithoutExt}_Excel_{counter}.xls";
                        exportPath = PathHelper.Excel(businessType, safeYear, safeTerm, newName);
                        counter++;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
                }

                using var templateStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var workbook = new HSSFWorkbook(templateStream);
                var abbyy = workbook.GetSheet("ABBYY") ?? throw new InvalidOperationException("Không tìm thấy sheet ABBYY.");

                if (businessTypeId == 3)
                {
                    int colMeta = ColumnLetterToIndex("C");
                    void SetMetaNH(int rowIndex, string? val)
                    {
                        var row = abbyy.GetRow(rowIndex) ?? abbyy.CreateRow(rowIndex);
                        var cell = row.GetCell(colMeta) ?? row.CreateCell(colMeta);
                        cell.SetCellValue(val ?? "");
                    }
                    SetMetaNH(2, meta.TenCongTy);
                    SetMetaNH(3, maCK);
                    SetMetaNH(4, kyBC);
                    SetMetaNH(5, namBC);
                    SetMetaNH(6, meta.TrangThaiKiemDuyet);
                    SetMetaNH(7, meta.LoaiBaoCao);
                    SetMetaNH(8, meta.TinhChatBaoCao);
                    SetMetaNH(9, meta.ThuocTinhKhac);
                    SetMetaNH(10, result.Currency);
                    SetMetaNH(11, meta.NgayCongBoBCTC);
                    SetMetaNH(12, metaDB.NgayKiemToan);
                }
                else
                {
                    var metaRow = abbyy.GetRow(1) ?? abbyy.CreateRow(1);
                    void SetCell(string colLetter, string? val)
                    {
                        int colIdx = ColumnLetterToIndex(colLetter);
                        var cell = metaRow.GetCell(colIdx) ?? metaRow.CreateCell(colIdx);
                        cell.SetCellValue(val ?? "");
                    }
                    SetCell("A", maCK);
                    SetCell("B", kyBC);
                    SetCell("C", namBC);
                    SetCell("D", meta.TrangThaiKiemDuyet);
                    SetCell("E", meta.LoaiBaoCao);
                    SetCell("F", meta.TinhChatBaoCao);
                    SetCell("G", meta.ThuocTinhKhac);
                    SetCell("H", result.Currency);
                    SetCell("I", meta.NgayCongBoBCTC);
                    SetCell("K", metaDB.NgayKiemToan);
                }

                try
                {
                    switch (businessTypeId)
                    {
                        case 1:
                        case 2:
                            FillAbbyyData(root, workbook, abbyy, "J", key);
                            break;
                        case 3:
                            FillAbbyyData_NH(root, workbook, abbyy, "I", key);
                            break;
                        case 5:
                            FillAbbyyData_BH(root, workbook, abbyy, "J", key);
                            break;
                        default:
                            FillAbbyyData(root, workbook, abbyy, "J", key);
                            break;
                    }

                    using var outFs = new FileStream(exportPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    workbook.Write(outFs);

                    Log.Information("[EXCEL][Export][OK] Xuất Excel: {Path}", exportPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[EXCEL][ExportData][ERROR] {Key}", key);
                }

                return exportPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEL][Export][FATAL] {Key}", key);
                return "";
            }
        }

        private static void FillAbbyyData_NH(JsonElement root, HSSFWorkbook wb, ISheet abbyy, string reportNormColumnLetter, string key)
        {
            try
            {
                Fill_NH_Internal(root, wb, abbyy, reportNormColumnLetter);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEL][FillAbbyyData_NH][ERROR] {Key}", key);
            }
        }

        private static void FillAbbyyData_BH(JsonElement root, HSSFWorkbook wb, ISheet abbyy, string reportNormColumnLetter, string key)
        {
            try
            {
                Fill_BH_Internal(root, wb, abbyy, reportNormColumnLetter);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEL][FillAbbyyData_BH][ERROR] {Key}", key);
            }
        }

        private static void FillAbbyyData(JsonElement root, HSSFWorkbook wb, ISheet abbyy, string reportNormColumnLetter, string key)
        {
            try
            {
                Fill_CPCK_Internal(root, wb, abbyy, reportNormColumnLetter);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EXCEL][FillAbbyyData][ERROR] {Key}", key);
            }
        }

        private static void Fill_NH_Internal(JsonElement root, HSSFWorkbook workbook, ISheet abbyy, string reportNormColumnLetter)
        {
            int reportNormCol = ColumnLetterToIndex(reportNormColumnLetter);
            var normRowMap = new Dictionary<string, int>();

            for (int r = 0; r <= abbyy.LastRowNum; r++)
            {
                var cell = abbyy.GetRow(r)?.GetCell(reportNormCol);
                if (cell == null) continue;
                string id = Regex.Replace(cell.ToString() ?? "", @"[^\d]", "");
                if (!string.IsNullOrEmpty(id)) normRowMap[id] = r;
            }

            var colsKy = new[] { ColumnLetterToIndex("C"), ColumnLetterToIndex("E"), ColumnLetterToIndex("G") };
            var colsLuyKe = new[] { ColumnLetterToIndex("D"), ColumnLetterToIndex("F"), ColumnLetterToIndex("H") };

            void FillArray(string prop)
            {
                if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var item in arr.EnumerateArray())
                {
                    if (!item.TryGetProperty("reportNormID", out var idProp)) continue;
                    string id = Regex.Replace(idProp.GetString() ?? "", @"[^\d]", "");
                    if (!normRowMap.TryGetValue(id, out int rowIndex)) continue;

                    var row = abbyy.GetRow(rowIndex) ?? abbyy.CreateRow(rowIndex);

                    if (item.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Object)
                    {
                        var pairs = vals.EnumerateObject().ToList();

                        if (pairs.Count >= 1 && TryGetNumber(pairs[0].Value, out double numeric))
                        {
                            var c = row.GetCell(colsKy[0]) ?? row.CreateCell(colsKy[0]);
                            c.SetCellType(CellType.Numeric);
                            c.SetCellValue(numeric);
                        }

                        if (pairs.Count >= 2)
                        {
                            pairs = pairs.OrderBy(x => x.Name).ToList();
                            for (int i = 0; i < Math.Min(pairs.Count, colsKy.Length); i++)
                            {
                                if (!TryGetNumber(pairs[i].Value, out double v)) continue;
                                var cell = row.GetCell(colsKy[i]) ?? row.CreateCell(colsKy[i]);
                                cell.SetCellType(CellType.Numeric);
                                cell.SetCellValue(v);
                            }

                            if (TryGetNumber(pairs.Last().Value, out double lk))
                            {
                                var lkCell = row.GetCell(colsLuyKe[0]) ?? row.CreateCell(colsLuyKe[0]);
                                lkCell.SetCellType(CellType.Numeric);
                                lkCell.SetCellValue(lk);
                            }
                        }
                    }
                }
            }

            FillArray("incomeStatement");
            FillArray("balanceSheet");
            FillArray("offBalanceSheet");
            FillArray("cashFlow");
        }

        private static void Fill_BH_Internal(JsonElement root, HSSFWorkbook workbook, ISheet abbyy, string reportNormColumnLetter)
        {
            int reportNormCol = ColumnLetterToIndex(reportNormColumnLetter);
            var normRowMap = new Dictionary<string, int>();

            for (int r = 0; r <= abbyy.LastRowNum; r++)
            {
                var cell = abbyy.GetRow(r)?.GetCell(reportNormCol);
                if (cell == null) continue;
                string id = Regex.Replace(cell.ToString() ?? "", @"[^\d]", "");
                if (!string.IsNullOrEmpty(id)) normRowMap[id] = r;
            }

            var colsKy = new[] { ColumnLetterToIndex("E"), ColumnLetterToIndex("G") };
            var colsLuyKe = new[] { ColumnLetterToIndex("F"), ColumnLetterToIndex("H") };

            void FillArray(string prop)
            {
                if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var item in arr.EnumerateArray())
                {
                    if (!item.TryGetProperty("reportNormID", out var idProp)) continue;

                    string id = Regex.Replace(idProp.GetString() ?? "", @"[^\d]", "");
                    if (!normRowMap.TryGetValue(id, out int rowIndex)) continue;

                    var row = abbyy.GetRow(rowIndex) ?? abbyy.CreateRow(rowIndex);

                    bool hasLuyKe =
                        (item.TryGetProperty("accumulate", out var acc) && acc.GetBoolean()) ||
                        (item.TryGetProperty("values", out var valsObj) &&
                         valsObj.TryGetProperty("luyKe", out _));

                    if (!item.TryGetProperty("values", out var vals) ||
                        vals.ValueKind != JsonValueKind.Object)
                        continue;

                    var pairs = vals.EnumerateObject()
                                    .Where(p => p.Name != "luyKe")
                                    .OrderByDescending(x => x.Name)
                                    .ToList();

                    for (int i = 0; i < Math.Min(pairs.Count, colsKy.Length); i++)
                    {
                        if (!TryGetNumber(pairs[i].Value, out double v)) continue;
                        var c = row.GetCell(colsKy[i]) ?? row.CreateCell(colsKy[i]);
                        c.SetCellType(CellType.Numeric);
                        c.SetCellValue(v);
                    }

                    if (hasLuyKe && vals.TryGetProperty("luyKe", out var lkProp))
                    {
                        if (TryGetNumber(lkProp, out double lk))
                        {
                            var cl = row.GetCell(colsLuyKe[0]) ?? row.CreateCell(colsLuyKe[0]);
                            cl.SetCellType(CellType.Numeric);
                            cl.SetCellValue(lk);
                        }
                    }
                }
            }

            FillArray("incomeStatement");
            FillArray("balanceSheet");
            FillArray("cashFlow");
        }

        private static void Fill_CPCK_Internal(JsonElement root, HSSFWorkbook workbook, ISheet abbyy, string reportNormColumnLetter)
        {
            int reportNormCol = ColumnLetterToIndex(reportNormColumnLetter);
            var normRowMap = new Dictionary<string, int>();

            for (int r = 0; r <= abbyy.LastRowNum; r++)
            {
                var cell = abbyy.GetRow(r)?.GetCell(reportNormCol);
                if (cell == null) continue;

                string id = Regex.Replace(cell.ToString() ?? "", @"[^\d]", "");
                if (!string.IsNullOrEmpty(id)) normRowMap[id] = r;
            }

            var detected = DetectDataColumns(workbook, reportNormCol);
            List<int> colsChinh = detected.colsChinh;
            if (colsChinh.Count == 0)
            {
                colsChinh.AddRange(new[] { ColumnLetterToIndex("D"), ColumnLetterToIndex("F"), ColumnLetterToIndex("H") });
            }

            void FillArray(string prop)
            {
                if (!root.TryGetProperty(prop, out var arr) ||
                    arr.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var item in arr.EnumerateArray())
                {
                    if (!item.TryGetProperty("reportNormID", out var idProp)) continue;

                    string id = Regex.Replace(idProp.GetString() ?? "", @"[^\d]", "");
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!normRowMap.TryGetValue(id, out int rowIndex)) continue;

                    var row = abbyy.GetRow(rowIndex) ?? abbyy.CreateRow(rowIndex);

                    if (item.TryGetProperty("values", out var vals) &&
                        vals.ValueKind == JsonValueKind.Object)
                    {
                        var ordered = vals.EnumerateObject().ToList();
                        for (int i = 0; i < ordered.Count && i < colsChinh.Count; i++)
                        {
                            var v = ordered[i].Value;
                            if (!TryGetNumber(v, out double num)) continue;
                            var c = row.GetCell(colsChinh[i]) ?? row.CreateCell(colsChinh[i]);
                            c.SetCellType(CellType.Numeric);
                            c.SetCellValue(num);
                        }
                    }
                }
            }

            FillArray("incomeStatement");
            FillArray("balanceSheet");
            FillArray("offBalanceSheet");
            FillArray("cashFlow");
        }

        private static (List<int> colsChinh, List<int> colsLuyKe) DetectDataColumns(HSSFWorkbook wb, int reportNormCol)
        {
            var found = new SortedSet<int>();
            var rx = new Regex(@"=ABBYY!\$([A-Z]+)\$\d+", RegexOptions.IgnoreCase);

            foreach (ISheet sheet in wb)
            {
                if (sheet.SheetName.Equals("ABBYY", StringComparison.OrdinalIgnoreCase)) continue;

                for (int r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
                {
                    var row = sheet.GetRow(r);
                    if (row == null) continue;

                    foreach (var cell in row.Cells)
                    {
                        if (cell.CellType != CellType.Formula) continue;
                        var m = rx.Match(cell.CellFormula);
                        if (m.Success)
                        {
                            int col = ColumnLetterToIndex(m.Groups[1].Value);
                            if (col != reportNormCol) found.Add(col);
                        }
                    }
                }
            }

            var cols = found.ToList();
            var mainCols = new List<int>();
            var accCols = new List<int>();

            foreach (var c in cols)
            {
                if (!accCols.Contains(c) && cols.Contains(c + 1))
                {
                    mainCols.Add(c);
                    accCols.Add(c + 1);
                }
            }

            return (mainCols, accCols);
        }

        private static bool TryGetNumber(JsonElement e, out double v)
        {
            v = 0;
            switch (e.ValueKind)
            {
                case JsonValueKind.Number:
                    return e.TryGetDouble(out v);
                case JsonValueKind.String:
                    var s = e.GetString() ?? "";
                    s = s.Trim();
                    bool neg = s.StartsWith("(") && s.EndsWith(")");
                    s = Regex.Replace(s, @"[^\d\.]", "");
                    if (double.TryParse(s, out v))
                    {
                        if (neg) v = -v;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static int ColumnLetterToIndex(string col)
        {
            col = col.ToUpper();
            int sum = 0;
            for (int i = 0; i < col.Length; i++)
                sum = sum * 26 + col[i] - 'A' + 1;
            return sum - 1;
        }

        private static string MakeSafeName(string name)
        {
            string noAccent = RemoveDiacritics(name);
            string clean = Regex.Replace(noAccent, @"[^A-Za-z0-9]+", "_");
            return clean.ToUpper();
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}