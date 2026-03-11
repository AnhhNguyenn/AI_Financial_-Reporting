using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace BCTC.BusinessLogic.AutoProcessorLogic
{
    public static class FileLogic
    {
        public static string? SelectBestPdfFile(string extractDir)
        {
            try
            {
                var pdfFiles = Directory
                    .GetFiles(extractDir, "*.pdf", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.Length)
                    .ToList();

                if (pdfFiles.Count == 0)
                {
                    Log.Warning("[FileLogic.SelectBestPdfFile] No PDF found in {Dir}", extractDir);
                    return null;
                }

                if (pdfFiles.Count == 1)
                {
                    Log.Information("[FileLogic.SelectBestPdfFile] 1 PDF found: {File}", pdfFiles[0].FullName);
                    return pdfFiles[0].FullName;
                }

                var top2 = pdfFiles.Take(2).ToList();
                string[] viKeywords = { "_vi", "_tv", "vi.", "tv.", "viet" };

                foreach (var f in top2)
                {
                    if (viKeywords.Any(k => f.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        Log.Information("[FileLogic.SelectBestPdfFile] Selected VI file: {File}", f.FullName);
                        return f.FullName;
                    }
                }

                Log.Information("[FileLogic.SelectBestPdfFile] Selected largest file: {File}", top2[0].FullName);
                return top2[0].FullName;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileLogic.SelectBestPdfFile] FAIL extractDir={Dir}", extractDir);
                return null;
            }
        }

        public static string? UnpackArchive(string archivePath, string extractDir)
        {
            Log.Information("[UNPACK][START] Archive={Arc}", archivePath);

            try
            {
                if (Directory.Exists(extractDir))
                {
                    try { Directory.Delete(extractDir, true); }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[UNPACK] Cannot delete old extractDir {Dir}", extractDir);
                    }
                }
                Directory.CreateDirectory(extractDir);

                string tempDir = Path.Combine(
                    Path.GetDirectoryName(extractDir)!,
                    "_tmp_" + Guid.NewGuid().ToString("N")
                );
                Directory.CreateDirectory(tempDir);

                try
                {
                    using (var archive = ArchiveFactory.Open(archivePath))
                    {
                        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                        {
                            try
                            {
                                entry.WriteToDirectory(tempDir, new ExtractionOptions
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                            catch (Exception exEntry)
                            {
                                Log.Warning(exEntry,
                                    "[UNPACK][EXTRACT-ENTRY][FAIL] Entry={Entry} Archive={Arc}",
                                    entry.Key, archivePath);
                            }
                        }
                    }
                }
                catch (Exception exOpen)
                {
                    Log.Error(exOpen, "[UNPACK][OPEN-ARCHIVE][FAIL] Cannot open {Arc}", archivePath);
                    try { Directory.Delete(tempDir, true); } catch { }
                    return null;
                }

                var pdfFiles = Directory
                    .GetFiles(tempDir, "*.pdf", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.Length)
                    .ToList();

                if (pdfFiles.Count == 0)
                {
                    Log.Warning("[UNPACK][NO-PDF] Archive={Arc}", archivePath);
                    try { Directory.Delete(tempDir, true); } catch { }
                    return null;
                }

                var largest = pdfFiles.Take(2).ToList();
                var file1 = largest[0];
                FileInfo? file2 = largest.Count > 1 ? largest[1] : null;

                if (file2 == null || file1.Length >= file2.Length * 1.40)
                {
                    string finalPath = Path.Combine(extractDir, file1.Name);
                    File.Copy(file1.FullName, finalPath, true);

                    Log.Information("[UNPACK][CHOOSE][RULE1-LARGEST] File={File} Size={Size}",
                        finalPath, file1.Length);

                    try { Directory.Delete(tempDir, true); } catch { }
                    Log.Information("[UNPACK][DONE] Selected={File}", finalPath);
                    return finalPath;
                }

                string[] viKeywords = { "vi", "_tv", "viet", "vietnam"};

                var viCandidate = largest.FirstOrDefault(f =>
                    viKeywords.Any(k =>
                        f.Name.ToLower().Contains(k)
                    )
                );

                var chosen = viCandidate ?? file1;

                if (viCandidate != null)
                {
                    Log.Information("[UNPACK][CHOOSE][RULE2-VI] File={Name}", chosen.Name);
                }
                else
                {
                    Log.Information("[UNPACK][CHOOSE][RULE2-FALLBACK] File={Name}", chosen.Name);
                }

                string finalPdf = Path.Combine(extractDir, chosen.Name);
                File.Copy(chosen.FullName, finalPdf, true);

                Directory.Delete(tempDir, true);

                Log.Information("[UNPACK][DONE] Selected={File}", finalPdf);
                return finalPdf;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[UNPACK][FATAL] Archive={Arc}", archivePath);
                return null;
            }
        }

        public static string GetBaseFileName(string? url, string maCK, string ky, int year)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var uri = new Uri(url);
                    var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);

                    if (!string.IsNullOrWhiteSpace(name) && name.Length < 60)
                    {
                        Log.Information("[FileLogic.GetBaseFileName] From URL: {Name}", name);
                        return name;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FileLogic.GetBaseFileName] URL parse fail: {Url}", url);
            }

            string fallback = $"{maCK}_{ky}_{year}".Replace(" ", "_");
            Log.Information("[FileLogic.GetBaseFileName] Fallback: {Name}", fallback);
            return fallback;
        }
    }
}
