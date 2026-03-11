using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils
{
    public static class CacheKeyBuilder
    {
        public static string BuildKey(string prefix, params object[] parameters)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("Prefix cannot be null or empty", nameof(prefix));

            if (parameters == null || parameters.Length == 0)
                return prefix;

            var parts = new string[parameters.Length + 1];
            parts[0] = prefix;

            for (int i = 0; i < parameters.Length; i++)
            {
                parts[i + 1] = parameters[i]?.ToString() ?? "null";
            }

            return string.Join(":", parts);
        }
    }
}
