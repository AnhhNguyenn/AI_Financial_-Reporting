using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BCTC.App.Services.MappingCache
{
    public static class MappingCacheKeyBuilder
    {
        /// <summary>
        /// Tạo cache key chính (bao gồm code)
        /// Format: MAP:V2:{StockCode}:{BusinessTypeId}:{ComponentType}:{Code}:{NormItemName}:{NormParentName}
        /// </summary>
        public static string BuildPrimary(
            string stockCode,
            int businessTypeId,
            int componentType,
            string itemName,
            string parentName,
            string code = null)
        {
            var sb = new StringBuilder("MAP:V2:");

            sb.Append(stockCode.ToUpperInvariant()).Append(":");
            sb.Append(businessTypeId).Append(":");
            sb.Append(componentType).Append(":");

            var normCode = string.IsNullOrWhiteSpace(code) ? "NULL" : code.Trim();
            var normItem = NormalizeText(itemName);
            var normParent = NormalizeText(parentName);

            sb.Append(normCode).Append(":");
            sb.Append(normItem).Append(":");
            sb.Append(normParent);

            return sb.ToString();
        }

        /// <summary>
        /// Tạo cache key phụ (không bao gồm code - để fallback search)
        /// </summary>
        public static string BuildSecondary(
            string stockCode,
            int businessTypeId,
            int componentType,
            string itemName,
            string parentName)
        {
            return BuildPrimary(stockCode, businessTypeId, componentType, itemName, parentName, null);
        }

        /// <summary>
        /// Normalize text để làm key: lowercase, remove accents, remove special chars
        /// </summary>
        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "NULL";

            // 1. Lowercase
            text = text.ToLowerInvariant();

            // 2. Remove Vietnamese accents
            text = RemoveVietnameseAccents(text);

            // 3. Remove extra spaces
            text = Regex.Replace(text, @"\s+", " ").Trim();

            // 4. Remove special characters (keep alphanumeric and spaces)
            text = Regex.Replace(text, @"[^a-z0-9\s]", "");

            // 5. Replace spaces with underscore
            text = text.Replace(" ", "_");

            // 6. Hash if too long (để tránh key quá dài)
            if (text.Length > 100)
            {
                text = HashString(text);
            }

            return text;
        }

        /// <summary>
        /// Remove Vietnamese accents
        /// </summary>
        private static string RemoveVietnameseAccents(string text)
        {
            var replacements = new (string pattern, string replacement)[]
            {
                (@"[àáạảãâầấậẩẫăằắặẳẵ]", "a"),
                (@"[èéẹẻẽêềếệểễ]", "e"),
                (@"[ìíịỉĩ]", "i"),
                (@"[òóọỏõôồốộổỗơờớợởỡ]", "o"),
                (@"[ùúụủũưừứựửữ]", "u"),
                (@"[ỳýỵỷỹ]", "y"),
                (@"[đ]", "d"),
                (@"[ÀÁẠẢÃÂẦẤẬẨẪĂẰẮẶẲẴ]", "A"),
                (@"[ÈÉẸẺẼÊỀẾỆỂỄ]", "E"),
                (@"[ÌÍỊỈĨ]", "I"),
                (@"[ÒÓỌỎÕÔỒỐỘỔỖƠỜỚỢỞỠ]", "O"),
                (@"[ÙÚỤỦŨƯỪỨỰỬỮ]", "U"),
                (@"[ỲÝỴỶỸ]", "Y"),
                (@"[Đ]", "D")
            };

            foreach (var (pattern, replacement) in replacements)
            {
                text = Regex.Replace(text, pattern, replacement);
            }

            return text;
        }

        /// <summary>
        /// Hash string nếu quá dài
        /// </summary>
        private static string HashString(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash)
                .Replace("+", "")
                .Replace("/", "")
                .Replace("=", "")
                .Substring(0, 32);
        }
    }
}