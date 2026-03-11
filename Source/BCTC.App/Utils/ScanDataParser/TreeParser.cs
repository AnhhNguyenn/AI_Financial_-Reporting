using MappingReportNorm.Utils.ScanDataParser.Models;
using System.Text.RegularExpressions;

namespace MappingReportNorm.Utils.ScanDataParser
{

    public class UppercaseLetterDetector : ILevelDetector
    {
        public int Level => 1;
        private static readonly Regex Pattern = new Regex(@"^([A-Z])\.\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            var match = Pattern.Match(text);
            if (!match.Success) return false;

            // Kiểm tra không phải số La Mã in hoa
            var letter = match.Groups[1].Value;
            return !IsRomanNumeral(letter);
        }

        public string ExtractPrefix(string text)
        {
            return Pattern.Match(text).Groups[1].Value;
        }

        private bool IsRomanNumeral(string s)
        {
            return new[] { "I", "V", "X", "L", "C", "D", "M" }.Contains(s);
        }
    }

    public class UppercaseRomanDetector : ILevelDetector
    {
        public int Level => 2;
        private static readonly Regex Pattern = new Regex(@"^([IVXLCDM]+)\.\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            var match = Pattern.Match(text);
            if (!match.Success) return false;

            var roman = match.Groups[1].Value;
            return IsValidRoman(roman);
        }

        public string ExtractPrefix(string text)
        {
            return Pattern.Match(text).Groups[1].Value;
        }

        private bool IsValidRoman(string s)
        {
            if (s.Length == 0) return false;
            return Regex.IsMatch(s, @"^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$");
        }
    }

    public class ArabicNumberDetector : ILevelDetector
    {
        public int Level => 3;
        private static readonly Regex Pattern = new Regex(@"^(\d+)\.\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            var match = Pattern.Match(text);
            if (!match.Success) return false;

            // Đảm bảo không phải dạng 1.1 hoặc 1.1.1
            var afterMatch = text.Substring(match.Length);
            return !Regex.IsMatch(afterMatch, @"^\d+\.");
        }

        public string ExtractPrefix(string text)
        {
            return Pattern.Match(text).Groups[1].Value;
        }
    }

    public class DecimalOneDetector : ILevelDetector
    {
        public int Level => 4;
        private static readonly Regex Pattern = new Regex(@"^(\d+\.\d+)\.\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            var match = Pattern.Match(text);
            if (!match.Success) return false;

            var prefix = match.Groups[1].Value;
            var parts = prefix.Split('.');

            // Đảm bảo chỉ có 2 phần (1.1) không phải 3 phần (1.1.1)
            return parts.Length == 2;
        }

        public string ExtractPrefix(string text)
        {
            return Pattern.Match(text).Groups[1].Value;
        }
    }

    public class DecimalTwoDetector : ILevelDetector
    {
        public int Level => 5;
        private static readonly Regex Pattern = new Regex(@"^(\d+\.\d+\.\d+)\.\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            return Pattern.IsMatch(text);
        }

        public string ExtractPrefix(string text)
        {
            return Pattern.Match(text).Groups[1].Value;
        }
    }

    public class LowercaseRomanDetector : ILevelDetector
    {
        public int Level => 6;
        private static readonly Regex Pattern = new Regex(@"^([ivxlcdm]+)\.\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            var match = Pattern.Match(text);
            if (!match.Success) return false;

            var roman = match.Groups[1].Value;
            return IsValidRoman(roman);
        }

        public string ExtractPrefix(string text)
        {
            return Pattern.Match(text).Groups[1].Value;
        }

        private bool IsValidRoman(string s)
        {
            if (s.Length == 0) return false;
            return Regex.IsMatch(s, @"^m{0,3}(cm|cd|d?c{0,3})(xc|xl|l?x{0,3})(ix|iv|v?i{0,3})$");
        }
    }

    public class LowercaseLetterDetector : ILevelDetector
    {
        public int Level => 7;
        private static readonly Regex Pattern = new Regex(@"^([a-z])\.\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            var match = Pattern.Match(text);
            if (!match.Success) return false;

            var letter = match.Groups[1].Value;
            // Kiểm tra không phải số La Mã thường
            return !IsRomanNumeral(letter);
        }

        public string ExtractPrefix(string text)
        {
            return Pattern.Match(text).Groups[1].Value;
        }

        private bool IsRomanNumeral(string s)
        {
            return new[] { "i", "v", "x", "l", "c", "d", "m" }.Contains(s);
        }
    }

    public class ParenthesisNumberDetector : ILevelDetector
    {
        public int Level => 8;
        private static readonly Regex Pattern = new Regex(@"^\((\d+)\)\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            return Pattern.IsMatch(text);
        }

        public string ExtractPrefix(string text)
        {
            return "(" + Pattern.Match(text).Groups[1].Value + ")";
        }
    }

    public class ParenthesisLetterDetector : ILevelDetector
    {
        public int Level => 9;
        private static readonly Regex Pattern = new Regex(@"^\(([a-z])\)\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            return Pattern.IsMatch(text);
        }

        public string ExtractPrefix(string text)
        {
            return "(" + Pattern.Match(text).Groups[1].Value + ")";
        }
    }

    public class DashDetector : ILevelDetector
    {
        public int Level => 10;
        private static readonly Regex Pattern = new Regex(@"^(–|-)\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            return Pattern.IsMatch(text);
        }

        public string ExtractPrefix(string text)
        {
            return Pattern.Match(text).Groups[1].Value;
        }
    }

    public class PlusDetector : ILevelDetector
    {
        public int Level => 11;
        private static readonly Regex Pattern = new Regex(@"^(\+)\s*", RegexOptions.Compiled);

        public bool IsMatch(string text)
        {
            return Pattern.IsMatch(text);
        }

        public string ExtractPrefix(string text)
        {
            return "+";
        }
    }

    public class AllUppercaseDetector : ILevelDetector
    {
        public int Level => 1;

        public bool IsMatch(string text)
        {
            // Chỉ áp dụng khi không có prefix khác
            var trimmed = text.Trim();
            if (string.IsNullOrEmpty(trimmed)) return false;

            // Kiểm tra toàn bộ text (không tính khoảng trắng) có phải in hoa không
            var letters = trimmed.Where(char.IsLetter).ToArray();
            if (letters.Length == 0) return false;

            return letters.All(char.IsUpper);
        }

        public string ExtractPrefix(string text)
        {
            return string.Empty;
        }
    }

    // ===== Level Detection Service =====
    public class LevelDetectionService
    {
        private readonly List<ILevelDetector> _detectors;

        public LevelDetectionService()
        {
            // Thứ tự ưu tiên: Level 5 -> 4 -> 3 (tránh match nhầm số thập phân)
            // Sau đó theo thứ tự level tăng dần
            _detectors = new List<ILevelDetector>
            {
                new UppercaseLetterDetector(),      // Level 1 (prefix rõ ràng)
                new UppercaseRomanDetector(),       // Level 2
                new DecimalTwoDetector(),           // Level 5 (ưu tiên cao)
                new DecimalOneDetector(),           // Level 4
                new ArabicNumberDetector(),         // Level 3
                new LowercaseRomanDetector(),       // Level 6
                new LowercaseLetterDetector(),      // Level 7
                new ParenthesisNumberDetector(),    // Level 8
                new ParenthesisLetterDetector(),    // Level 9
                new DashDetector(),                 // Level 10
                new PlusDetector(),                 // Level 11
                new AllUppercaseDetector()          // Level 1 (fallback, không có prefix)
            };
        }

        public (int level, string prefix) Detect(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (0, string.Empty);

            var trimmed = text.Trim();

            foreach (var detector in _detectors)
            {
                if (detector.IsMatch(trimmed))
                {
                    return (detector.Level, detector.ExtractPrefix(trimmed));
                }
            }

            return (0, string.Empty);
        }
    }

    // ===== Stack-based Parser =====
    public class TreeParser
    {
        private readonly LevelDetectionService _levelService;

        public TreeParser()
        {
            _levelService = new LevelDetectionService();
        }

        public List<TreeNode> Parse(List<ScanItem> items)
        {
            if (items == null || items.Count == 0)
                return new List<TreeNode>();

            var result = new List<TreeNode>();
            var stack = new Stack<TreeNode>();

            foreach (var item in items)
            {
                var (level, prefix) = _levelService.Detect(item.Text);

                // Xử lý dòng không có prefix rõ ràng (level = 0)
                if (level == 0)
                {
                    // Tìm parent gần nhất có level từ 1-9
                    var parent = FindParentForNonPrefixLine(stack);

                    var node = new TreeNode
                    {
                        Index = item.Index,
                        Text = item.Text,
                        Level = 0, // Không có level (không tham gia stack hierarchy)
                        Prefix = string.Empty,
                        ParentIndex = parent?.Index ?? 0,
                        ReportNormID = item.ReportNormID,
                        ParentText = item.ParentText
                    };

                    // KHÔNG push vào stack vì không có cấp
                    result.Add(node);
                    continue;
                }

                var normalNode = new TreeNode
                {
                    Index = item.Index,
                    Text = item.Text,
                    Level = level,
                    Prefix = prefix,
                    ParentIndex = 0,
                    ReportNormID = item.ReportNormID,
                    ParentText = item.ParentText
                };

                // Pop stack cho đến khi tìm được parent phù hợp
                while (stack.Count > 0 && stack.Peek().Level >= level)
                {
                    stack.Pop();
                }

                // Gán parent
                if (stack.Count > 0)
                {
                    normalNode.ParentIndex = stack.Peek().Index;
                }

                stack.Push(normalNode);
                result.Add(normalNode);
            }

            return result;
        }

        private TreeNode FindParentForNonPrefixLine(Stack<TreeNode> stack)
        {
            // Tìm node gần nhất có level từ 1-9 (trên đỉnh stack)
            foreach (var node in stack)
            {
                if (node.Level >= 1 && node.Level <= 9)
                {
                    return node;
                }
            }

            return null;
        }
    }

}