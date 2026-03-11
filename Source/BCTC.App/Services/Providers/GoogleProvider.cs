using MappingReportNorm.Interfaces.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MappingReportNorm.Services.Providers
{
    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate> Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata UsageMetadata { get; set; }
    }

    public class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent Content { get; set; }
    }

    public class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; }
    }

    public class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("thoughtsTokenCount")]
        public int ThoughtsTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }

    public class GoogleProvider : IAIModelProvider
    {
        private readonly string _apiKey;
        private readonly string _modelType;
        private readonly HttpClient _httpClient;
        private readonly int _maxRetries;
        private readonly int _retryDelayMilliseconds;

        public GoogleProvider(string apiKey, string modelType, int timeoutSeconds, int maxRetries, int retryDelayMilliseconds)
        {
            _apiKey = apiKey;
            _modelType = modelType;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            _maxRetries = maxRetries;
            _retryDelayMilliseconds = retryDelayMilliseconds;
        }

        public async Task<(string responseContent, int inputTokens, int outputTokens, int totalTokens)> GetCompletionAsync(
            string systemPrompt,
            string userPrompt,
            object responseFormat)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelType}:generateContent?key={_apiKey}";

            var requestBody = new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[]
                {
                new { role = "user", parts = new[] { new { text = userPrompt } } }
            },
                generationConfig = new
                {
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = responseFormat
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Google API Error: {response.StatusCode} - {responseString}");
                    }

                    var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseString, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var textContent = geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "{}";
                    int inputTokens = geminiResponse?.UsageMetadata?.PromptTokenCount ?? 0;
                    int outputTokens = ((geminiResponse?.UsageMetadata?.CandidatesTokenCount ?? 0) + (geminiResponse?.UsageMetadata?.ThoughtsTokenCount ?? 0));
                    int totalTokens = geminiResponse?.UsageMetadata?.TotalTokenCount ?? 0;

                    return (textContent, inputTokens, outputTokens, totalTokens);
                }
                catch (Exception ex)
                {
                    if (attempt >= _maxRetries)
                    {
                        throw new Exception($"Failed after {_maxRetries} attempts. Original error: {ex.Message}");
                    }

                    Console.WriteLine($"Lần thử {attempt} thất bại: {ex.Message}. Đang thử lại sau 10 giây...");

                    await Task.Delay(_retryDelayMilliseconds);
                }
            }
        }
    }
}
