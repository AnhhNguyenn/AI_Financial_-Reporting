using BCTC.DataAccess.Models.Enum;
using MappingReportNorm.Utils.DataChunking.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils.DataChunking
{
    public class DataChunkingService<T>
    {
        private readonly string _fieldId;
        private readonly string _fieldText;
        private readonly PropertyInfo _idProperty;
        private readonly PropertyInfo _textProperty;

        public DataChunkingService(string fieldId, string fieldText)
        {
            _fieldId = fieldId;
            _fieldText = fieldText;

            var type = typeof(T);
            _idProperty = type.GetProperty(fieldId);
            _textProperty = type.GetProperty(fieldText);

            if (_idProperty == null)
                throw new ArgumentException($"Property {fieldId} not found on type {type.Name}");
            if (_textProperty == null)
                throw new ArgumentException($"Property {fieldText} not found on type {type.Name}");
        }

        public List<T> GetChunk(List<T> items, ReportTemplate template, int chunkIndex)
        {
            if (items == null || items.Count == 0)
                return new List<T>();

            var config = ChunkRuleProvider.GetConfiguration(template);

            if (chunkIndex < 0 || chunkIndex >= config.Steps.Count)
                throw new ArgumentException($"Invalid chunk index: {chunkIndex}");

            return ExtractChunk(items, config.Steps, chunkIndex);
        }

        private List<T> ExtractChunk(List<T> items, List<ChunkStep> steps, int chunkIndex)
        {
            int startIndex = 0;

            // Process steps before the target chunk to find start position
            for (int stepIdx = 0; stepIdx < chunkIndex; stepIdx++)
            {
                var step = steps[stepIdx];
                int matchIndex = FindMatchingIndex(items, startIndex, step);

                if (matchIndex == -1)
                {
                    // No match found, cannot proceed
                    return new List<T>();
                }

                // Move start position based on whether we include the matched item
                //startIndex = step.IncludeEndItem ? matchIndex : matchIndex + 1;
                startIndex = step.IncludeEndItem ? matchIndex : matchIndex;
            }

            // Process the target chunk
            var targetStep = steps[chunkIndex];

            if (targetStep.Rules.Count == 0)
            {
                // Last step - take all remaining items
                return items.Skip(startIndex).ToList();
            }

            int endIndex = FindMatchingIndex(items, startIndex, targetStep);

            if (endIndex == -1)
            {
                // No end found, take all remaining
                return items.Skip(startIndex).ToList();
            }

            // Extract chunk
            int count = endIndex - startIndex;
            if (targetStep.IncludeEndItem)
            {
                count++;
            }

            return items.Skip(startIndex).Take(count).ToList();
        }

        private int FindMatchingIndex(List<T> items, int startIndex, ChunkStep step)
        {
            if (step.Rules.Count == 0)
                return items.Count; // End of list

            for (int i = startIndex; i < items.Count; i++)
            {
                var item = items[i];

                // Try each rule (OR condition)
                foreach (var rule in step.Rules)
                {
                    if (MatchesRule(item, rule))
                    {
                        return i;
                    }
                }
            }

            return -1; // No match found
        }

        private bool MatchesRule(T item, ChunkRule rule)
        {
            var idValue = _idProperty.GetValue(item);
            var textValue = _textProperty.GetValue(item)?.ToString() ?? string.Empty;

            // Priority 1: Check ID match
            if (rule.Id.HasValue && idValue != null)
            {
                if (Convert.ToInt32(idValue) == rule.Id.Value)
                {
                    return true;
                }
            }

            // Priority 2: Check text match (only if ID is null)
            if (idValue == null && rule.Texts.Count > 0)
            {
                return MatchesText(textValue, rule.Texts, rule.RequireAllTexts);
            }

            return false;
        }

        private bool MatchesText(string text, List<string> patterns, bool requireAll)
        {
            if (string.IsNullOrEmpty(text) || patterns.Count == 0)
                return false;

            var normalizedText = NormalizeText(text);

            if (requireAll)
            {
                // AND condition - all patterns must be present
                return patterns.All(pattern => normalizedText.Contains(NormalizeText(pattern)));
            }
            else
            {
                // OR condition - at least one pattern must be present
                return patterns.Any(pattern => normalizedText.Contains(NormalizeText(pattern)));
            }
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Convert to lowercase and normalize Unicode
            return text.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        }
    }
}
