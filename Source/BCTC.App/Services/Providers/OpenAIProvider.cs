using MappingReportNorm.Interfaces.Services;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Services.Providers
{
    public class OpenAIProvider : IAIModelProvider
    {
        private readonly OpenAIClient _client;
        private readonly string _modelType;
        private readonly int _maxRetries;
        private readonly int _retryDelayMilliseconds;

        public OpenAIProvider(string apiKey, string modelType, int timeoutSeconds, int maxRetries, int retryDelayMilliseconds)
        {
            _modelType = modelType;

            var options = new OpenAIClientOptions
            {
                NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            _client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
            _maxRetries = maxRetries;
            _retryDelayMilliseconds = retryDelayMilliseconds;
        }

        public async Task<(string responseContent, int inputTokens, int outputTokens, int totalTokens)> GetCompletionAsync(
            string systemPrompt,
            string userPrompt,
            object responseFormat)
        {
            var chatRequest = new ChatCompletionOptions
            {
                //Temperature = 0.1f,
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: "financial_mapping",
                    jsonSchema: BinaryData.FromObjectAsJson(responseFormat),
                    jsonSchemaIsStrict: true
                )
            };

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    ChatCompletion completion = await _client.GetChatClient(_modelType)
                        .CompleteChatAsync(messages, chatRequest);

                    return (
                        completion.Content[0].Text,
                        completion.Usage.InputTokenCount,
                        completion.Usage.OutputTokenCount,
                        completion.Usage.TotalTokenCount
                    );
                }
                catch (Exception ex)
                {
                    if (attempt >= _maxRetries)
                    {
                        throw new Exception($"OpenAI API failed after {_maxRetries} attempts. Error: {ex.Message}");
                    }

                    Console.WriteLine($"[OpenAI] Lần thử {attempt} thất bại. Đang thử lại sau 10 giây... Lỗi: {ex.Message}");
                    await Task.Delay(_retryDelayMilliseconds);
                }
            }
        }
    }
}
