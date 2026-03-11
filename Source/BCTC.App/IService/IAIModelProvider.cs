using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Interfaces.Services
{
    public interface IAIModelProvider
    {
        Task<(string responseContent, int inputTokens, int outputTokens, int totalTokens)> GetCompletionAsync(
            string systemPrompt,
            string userPrompt,
            object responseFormat
        );
    }
}
