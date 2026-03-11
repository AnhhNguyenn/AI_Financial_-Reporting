using BCTC.DataAccess.Models;
using Microsoft.Data.SqlClient;
using Serilog;
using System.Data;
using System.Globalization;
using System.Text;

namespace BCTC.App.Services.DatabaseServices
{
    public static class SqlImportHelper
    {
        public static async Task ImportToDatabaseAsync(ExtractResult data, string connStr)
        {
            const string tag = "[SqlImportHelper.ImportToDatabase]";

            if (data == null)
            {
                Log.Warning($"{tag} Không có dữ liệu để ghi.");
                return;
            }

            NormalizeMeta(data);
            var meta = data.Meta ?? new Meta();
            var metaDB = data.MetaDB ?? new MetaDB();

            string maCK = meta.MaCongTy ?? "";
            string ky = meta.KyBaoCao ?? "";
            int nam = meta.Nam;
            string companyName = meta.TenCongTy ?? "";
            string displayName = $"{maCK} - {companyName} - {ky} - {nam}";

            Log.Information($"{tag} Bắt đầu import {displayName}");

            try
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                using var cmd = new SqlCommand("dbo.dta_ImpReportData", conn)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@iReportTermCode", ky);
                cmd.Parameters.AddWithValue("@iCompanyCode", maCK);
                cmd.Parameters.AddWithValue("@iAuditStatusCode", meta.TrangThaiKiemDuyet ?? "");
                cmd.Parameters.AddWithValue("@iCurrencyUnitCode", NormalizeCurrency(data.Currency ?? ""));
                cmd.Parameters.AddWithValue("@iReportDate", DateTime.Now);
                cmd.Parameters.AddWithValue("@iYearPeriod", nam);
                cmd.Parameters.AddWithValue("@iAdjustedCode", meta.ThuocTinhKhac ?? "");
                cmd.Parameters.AddWithValue("@iUnitedCode", meta.TinhChatBaoCao ?? "");
                cmd.Parameters.AddWithValue("@iAbstractedCode", meta.LoaiBaoCao ?? "");
                cmd.Parameters.AddWithValue("@iCtyKiemToan", metaDB.CtyKiemToan ?? "");
                cmd.Parameters.AddWithValue("@iDatePubDepartment", ParseOrDefault(meta.NgayCongBoBCTC ?? ""));
                cmd.Parameters.AddWithValue("@iDateAudited", ParseOrDefault(metaDB.NgayKiemToan ?? ""));
                cmd.Parameters.AddWithValue("@iAuditedNote", metaDB.AuditedNote ?? "");
                cmd.Parameters.AddWithValue("@iNote", metaDB.Note ?? "");
                cmd.Parameters.AddWithValue("@iIsReplace", 0);

                var dtDetail = new DataTable();
                dtDetail.Columns.Add("ReportDataDetailID", typeof(int));
                dtDetail.Columns.Add("ReportDataID", typeof(int));
                dtDetail.Columns.Add("ReportNormID", typeof(int));
                dtDetail.Columns.Add("Value", typeof(decimal));
                dtDetail.Columns.Add("Comment", typeof(string));

                int rowCount = 0;
                var allSections = new List<Row>();
                if (data.IncomeStatement != null) allSections.AddRange(data.IncomeStatement);
                if (data.BalanceSheet != null) allSections.AddRange(data.BalanceSheet);
                if (data.CashFlow != null) allSections.AddRange(data.CashFlow);
                if (data.OffBalanceSheet != null) allSections.AddRange(data.OffBalanceSheet);

                HashSet<int> checkDup = new HashSet<int>();

                foreach (var r in allSections)
                {
                    if (int.TryParse(r.ReportNormID, out int normId))
                    {
                        if (checkDup.Contains(normId))
                        {
                            string errorMsg = $"{tag} [CRITICAL/ABORT] Phát hiện dữ liệu TRÙNG LẶP! ReportNormID={normId} (Item: {r.Item}) xuất hiện nhiều lần. Hủy import file này.";
                            Log.Error(errorMsg);

                            throw new InvalidDataException(errorMsg);
                        }

                        checkDup.Add(normId);

                        decimal val = 0;
                        if (r.Values != null && r.Values.Count > 0)
                        {
                            string yearKeyword = nam.ToString();
                            var match = r.Values.FirstOrDefault(kv => kv.Key.Contains(yearKeyword));
                            if (match.Key != null)
                            {
                                if (match.Value.HasValue)
                                    val = Convert.ToDecimal(match.Value.Value);
                                else
                                    val = 0;
                            }
                            else
                            {
                                var firstVal = r.Values.Values.FirstOrDefault();
                                if (firstVal.HasValue)
                                    val = Convert.ToDecimal(firstVal.Value);
                                else
                                    val = 0;
                            }
                            dtDetail.Rows.Add(0, 0, normId, val, "");
                            rowCount++;
                        }
                    }
                }
                var pDetail = cmd.Parameters.AddWithValue("@iReportDetail", dtDetail);
                pDetail.SqlDbType = SqlDbType.Structured;
                pDetail.TypeName = "dbo.ReportDataDetailsType";

                var outMsg = new SqlParameter("@oErrMsg", SqlDbType.NVarChar, 500)
                {
                    Direction = ParameterDirection.Output
                };
                cmd.Parameters.Add(outMsg);

                Log.Information($"{tag} Data OK ({rowCount} rows). Gọi SP dbo.dta_ImpReportData_AI...");

                await cmd.ExecuteNonQueryAsync();

                var msg = outMsg.Value?.ToString();

                if (!string.IsNullOrWhiteSpace(msg))
                    Log.Warning($"{tag} {displayName}: {msg}");
                else
                    Log.Information($"{tag} {displayName} → {rowCount} dòng đã import thành công.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{tag} Lỗi import {displayName}");
                throw;
            }

            Log.Information($"{tag} Kết thúc import {displayName}");
        }

        public static async Task MarkFileAsProcessedAsync(int fileInfoId, string connStr)
        {
            const string tag = "[SqlImportHelper.MarkFileAsProcessed]";

            try
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                const string sql = @"
                    UPDATE FileProcessingStatus
                    SET ProcessingStatus = 1, LastUpdate = GETDATE()
                    WHERE FileInfoID = @FileInfoID";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@FileInfoID", fileInfoId);

                int affected = await cmd.ExecuteNonQueryAsync();

                if (affected > 0)
                    Log.Information($"{tag} Đánh dấu hoàn tất FileInfoID={fileInfoId}");
                else
                    Log.Warning($"{tag} Không tìm thấy FileInfoID={fileInfoId}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{tag} Lỗi cập nhật ProcessingStatus cho FileInfoID={fileInfoId}");
            }
        }

        private static DateTime ParseOrDefault(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return DateTime.Now;

            raw = raw.Trim();

            var vi = new CultureInfo("vi-VN");
            if (DateTime.TryParse(raw, vi, DateTimeStyles.None, out var dtVi))
                return dtVi;

            string[] formats =
            {
                "dd/MM/yyyy", "d/M/yyyy",
                "yyyy-MM-dd", "yyyy/MM/dd",
                "dd-MM-yyyy", "d-M-yyyy"
            };

            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;

            var m = System.Text.RegularExpressions.Regex.Match(
                raw, @"(\d{1,2}).*?(\d{1,2}).*?(\d{4})");

            if (m.Success)
            {
                int d = int.Parse(m.Groups[1].Value);
                int mm = int.Parse(m.Groups[2].Value);
                int y = int.Parse(m.Groups[3].Value);

                try
                {
                    return new DateTime(y, mm, d);
                }
                catch { }
            }

            return DateTime.Now;
        }

        private static void NormalizeMeta(ExtractResult data)
        {
            if (data.Meta == null)
                data.Meta = new Meta();

            var meta = data.Meta;

            if (!string.IsNullOrWhiteSpace(meta.LoaiBaoCao))
            {
                string v = meta.LoaiBaoCao.Trim().ToUpperInvariant();

                if (v.Contains("CHI TIẾT") || v.Contains("CHI TIET"))
                    meta.LoaiBaoCao = "CT";
                else if (v.Contains("TÓM TẮT") || v.Contains("TOM TAT"))
                    meta.LoaiBaoCao = "TT";
                else
                    meta.LoaiBaoCao = null;
            }
        }

        private static string NormalizeCurrency(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "VND";

            value = value.Normalize(NormalizationForm.FormD);
            value = new string(value.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            value = value.Normalize(NormalizationForm.FormC).Trim().ToUpperInvariant();
            value = string.Concat(value.Where(c => !char.IsControl(c)));
            value = value.Replace("₫", "").Replace("Đ", "D").Replace("  ", " ");

            string[] vndKeywords =
            {
                "VND", "VNĐ", "DONG", "VIETNAM", "VIET NAM",
                "DONG VIETNAM", "VIETNAM DONG", "TIEN", "TIEN VN",
                "TIEN VIETNAM", "TIEN VIET NAM"
            };

            foreach (var kw in vndKeywords)
                if (value.Contains(kw)) return "VND";

            if (value.Contains("USD") || value.Contains("DOLLAR") || value.Contains("$"))
                return "USD";

            return "VND";
        }
    }
}