using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Serilog;

namespace BCTC.App.Services.ChunkServices
{
    public static class PdfChunker
    {
        private static readonly string BaseDir;
        private static readonly string LimitedDir;
        private static readonly string ChunkDir;

        static PdfChunker()
        {
            BaseDir = AppContext.BaseDirectory;
            if (BaseDir.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            {
                var root = BaseDir.Split(new[] { "\\bin\\" }, StringSplitOptions.None)[0];
                BaseDir = Path.Combine(root, "wwwroot");
            }
            LimitedDir = Path.Combine(BaseDir, "pdf", "limited");
            ChunkDir = Path.Combine(BaseDir, "pdf", "chunks");
            if (!Directory.Exists(LimitedDir)) Directory.CreateDirectory(LimitedDir);
            if (!Directory.Exists(ChunkDir)) Directory.CreateDirectory(ChunkDir);
            Log.Information("[PdfChunker.Init] BaseDir = {Dir}", BaseDir);
        }

        public static string ExtractFirstPages(Stream originalPdf, int firstPages, string fileKey)
        {
            const string tag = "[PdfChunker.ExtractFirstPages]";

            try
            {
                if (firstPages <= 0)
                    throw new ArgumentOutOfRangeException(nameof(firstPages));

                if (originalPdf.CanSeek)
                    originalPdf.Position = 0;

                using var src = PdfReader.Open(originalPdf, PdfDocumentOpenMode.Import);

                int max = Math.Min(firstPages, src.PageCount);
                var outDoc = new PdfDocument();

                for (int i = 0; i < max; i++)
                    outDoc.AddPage(src.Pages[i]);

                string outPath = Path.Combine(LimitedDir, $"{fileKey}_clip.pdf");

                using (var fs = new FileStream(outPath, FileMode.Create))
                {
                    outDoc.Save(fs, false);
                }

                Log.Information($"{tag} Saved clip {outPath} ({max}/{src.PageCount})");
                return outPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{tag} Lỗi ExtractFirstPages");
                throw;
            }
        }

        public static List<string> SplitToChunks(string limitedPdfPath, int pagesPerChunk, string fileKey)
        {
            const string tag = "[PdfChunker.SplitToChunks]";

            var list = new List<string>();

            try
            {
                if (pagesPerChunk <= 0)
                    throw new ArgumentOutOfRangeException(nameof(pagesPerChunk));

                using var src = PdfReader.Open(limitedPdfPath, PdfDocumentOpenMode.Import);

                int total = src.PageCount;
                int chunkIndex = 1;

                for (int start = 0; start < total; start += pagesPerChunk)
                {
                    int end = Math.Min(start + pagesPerChunk, total);

                    var part = new PdfDocument();
                    for (int i = start; i < end; i++)
                        part.AddPage(src.Pages[i]);

                    string chunkPath = Path.Combine(ChunkDir, $"{fileKey}_chunk_{chunkIndex}.pdf");

                    using (var fs = new FileStream(chunkPath, FileMode.Create))
                    {
                        part.Save(fs, false);
                    }

                    list.Add(chunkPath);
                    chunkIndex++;
                }

                Log.Information($"{tag} {fileKey} tạo {list.Count} chunks");
                return list;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{tag} Lỗi SplitToChunks");
                return list;
            }
        }

        public static List<string> SplitFirstPages(Stream originalPdf, int firstPages, int pagesPerChunk, string fileKey)
        {
            const string tag = "[PdfChunker.SplitFirstPages]";

            try
            {
                string limitedPath = ExtractFirstPages(originalPdf, firstPages, fileKey);
                var chunks = SplitToChunks(limitedPath, pagesPerChunk, fileKey);
                return chunks;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{tag} Lỗi SplitFirstPages");
                return new List<string>();
            }
        }
    }
}
