using System.Text;
using System.Text.Json;
using Serilog;

namespace BCTC.App.Services.GeminiServices
{
    public partial class GeminiService
    {
        private async Task<string> UploadFileAsync(byte[] bytes, string fileName, string mime, string apiKey, CancellationToken ct)
        {
            try
            {
                Log.Information("[GEMINI][Upload][START] File={File} Size={Size}", fileName, bytes.Length);

                // 1. Init Session
                var startReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/upload/v1beta/files?key={apiKey}");
                startReq.Headers.Add("X-Goog-Upload-Protocol", "resumable");
                startReq.Headers.Add("X-Goog-Upload-Command", "start");
                startReq.Headers.Add("X-Goog-Upload-Header-Content-Length", bytes.Length.ToString());
                startReq.Headers.Add("X-Goog-Upload-Header-Content-Type", mime);

                var meta = new { file = new { display_name = fileName } };
                startReq.Content = new StringContent(JsonSerializer.Serialize(meta), Encoding.UTF8, "application/json");

                var startRes = await _http.SendAsync(startReq, ct);
                if (!startRes.IsSuccessStatusCode) return "";

                var uploadUrl = startRes.Headers.GetValues("X-Goog-Upload-URL").FirstOrDefault();
                if (string.IsNullOrEmpty(uploadUrl)) return "";

                // 2. Upload Bytes
                var upReq = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                upReq.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
                upReq.Headers.Add("X-Goog-Upload-Offset", "0");
                upReq.Content = new ByteArrayContent(bytes);

                var upRes = await _http.SendAsync(upReq, ct);
                if (!upRes.IsSuccessStatusCode) return "";

                var json = await upRes.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                // Trả về URI: https://generativelanguage.googleapis.com/v1beta/files/abc...
                return doc.RootElement.GetProperty("file").GetProperty("uri").GetString() ?? "";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GEMINI][Upload][FAIL] File={File}", fileName);
                return string.Empty;
            }
        }

        private async Task<bool> WaitForFileActiveAsync(string fileUri, string apiKey, CancellationToken ct)
        {
            string fileId = fileUri.Split('/').Last();
            string checkUrl = $"{BaseUrl}/v1beta/files/{fileId}";

            for (int i = 0; i < 20; i++)
            {
                try
                {
                    await Task.Delay(3000, ct);
                    var req = new HttpRequestMessage(HttpMethod.Get, checkUrl);
                    req.Headers.Add("x-goog-api-key", apiKey);

                    var res = await _http.SendAsync(req, ct);
                    if (!res.IsSuccessStatusCode) continue;

                    var json = await res.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);

                    // State: PROCESSING | ACTIVE | FAILED
                    string state = doc.RootElement.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";

                    if (state == "ACTIVE") return true;
                    if (state == "FAILED") return false;
                }
                catch { }
            }
            return false; // Timeout
        }
    }
}