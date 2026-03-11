using BCTC.DataAccess.Models;
using Serilog;
using System.Text.Json;

namespace BCTC.App.Services.GeminiServices
{
    public partial class GeminiService
    {
        private static string FixJson(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return "{}";

                string cleaned = text.Replace("```json", "").Replace("```", "").Trim();

                int firstBrace = cleaned.IndexOf('{');
                int firstBracket = cleaned.IndexOf('[');

                int start = -1;
                if (firstBrace == -1) start = firstBracket;
                else if (firstBracket == -1) start = firstBrace;
                else start = Math.Min(firstBrace, firstBracket);

                int lastBrace = cleaned.LastIndexOf('}');
                int lastBracket = cleaned.LastIndexOf(']');

                int end = Math.Max(lastBrace, lastBracket);

                if (start >= 0 && end > start)
                {
                    return cleaned.Substring(start, end - start + 1);
                }

                return cleaned;
            }
            catch (Exception ex)
            {
                Log.Warning($"[JSON][FIX] Lỗi khi clean string: {ex.Message}");
                return "{}";
            }
        }

        private static JsonElement ParseLenientOrEmpty(string raw, out string used)
        {
            used = FixJson(raw);
            try
            {
                var options = new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = 64
                };

                using var doc = JsonDocument.Parse(used, options);
                return doc.RootElement.Clone();
            }
            catch (Exception)
            {
                return JsonDocument.Parse("{}").RootElement.Clone();
            }
        }
    }
}