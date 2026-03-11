using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using BCTC.DataAccess.Models;
using Serilog;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Data;
using OpenCvSharp;
using Serilog.Events;
using NPOI.POIFS.Properties;
using NPOI.OpenXmlFormats.Spreadsheet;
using MappingReportNorm.Models;
using MappingReportNorm.Services;
using PdfSharpCore.Drawing.BarCodes;
using MathNet.Numerics.Providers.LinearAlgebra;
using static iText.StyledXmlParser.Jsoup.Select.Evaluator;
using SharpCompress.Common;
using iText.StyledXmlParser.Jsoup.Parser;

namespace BCTC.App.Utils
{
    public class IDError
    {
        public int ReportNormID { get; set; }
        public string SheetCode { get; set; } = string.Empty;
    }
    public class FormulaDefinition
    {
        public int? ReportNormID { get; set; }
        public string Formula { get; set; }
        public string SheetCode { get; set; } = string.Empty;
    }
    public class SheetKeyValue
    {
        public int ScanIndex { get; set; }
        public int? ReportNormID { get; set; }
        public string Item {  get; set; }
        public string SheetCode { get; set; } = string.Empty;
        public Dictionary<string, decimal?> Values { get; set; } = new();
        public bool IsUpdatePast { get; set; } = false;
    }
    public class ResultCheckFormula
    {
        public List<FormulaError> FormulaErrors = new List<FormulaError>();
        public List<int> IDCorrects = new List<int>();
        public List<int> IDDefaults = new List<int>();
    }
    //public class FormulaError
    //{
    //    public string SheetCode { get; set; }
    //    public int ParentID { get; set; }
    //    public string Formula { get; set; }
    //    public Dictionary<string, decimal?> ParentValues { get; set; }
    //    public Dictionary<int, Dictionary<string, decimal?>> ChildrenValues { get; set; }
    //    public Dictionary<string, decimal?> Differences { get; set; }
    //    //public Dictionary<int, string> RelatedFormulas { get; set; } = new();
    //}
    public class Item
    {
        public int ID { get; set; }
        public int ScanIndex { get; set; }
        public Dictionary<string, decimal?> Values { get; set; }
        public bool IsUpdatePast { get; set; } = false;
    }
    public class FormulaError
    {
        public string SheetCode { get; set; }
        public string Formula { get; set; }
        public Item Parent { get; set; }
        public List<Item> Childs { get; set; }
        public Dictionary<string, decimal?> Differences { get; set; }
    }
    public class CalculationContext
    {
        private Dictionary<int, decimal?> _values;
        public CalculationContext(Dictionary<int, decimal?> values)
        {
            _values = values ?? new Dictionary<int, decimal?>();
        }
        public void SetValue(int id, decimal value)
        {
            _values[id] = value;
        }
        public decimal? GetValue(int id)
        {
            return _values.ContainsKey(id) ? _values[id] : null;
        }
    }
    public abstract class Expression
    {
        public abstract object Evaluate(CalculationContext context);
        public abstract List<int> GetDependencies();
    }
    public class LiteralExpression : Expression
    {
        private readonly decimal _value;
        public LiteralExpression(decimal value) => _value = value;
        public override object Evaluate(CalculationContext _) => _value;
        public override List<int> GetDependencies() => new();
    }
    public class VariableExpression : Expression
    {
        public int Id { get; }
        public VariableExpression(int id) => Id = id;
        public override object Evaluate(CalculationContext ctx) => ctx.GetValue(Id);
        public override List<int> GetDependencies() => new() { Id };
    }
    public class BinaryExpression : Expression
    {
        private readonly Expression _l;
        private readonly Expression _r;
        private readonly string _op;

        public BinaryExpression(Expression l, string op, Expression r)
        {
            _l = l; _op = op; _r = r;
        }

        public override object Evaluate(CalculationContext ctx)
        {
            decimal l = Convert.ToDecimal(_l.Evaluate(ctx));
            decimal r = Convert.ToDecimal(_r.Evaluate(ctx));

            return _op switch
            {
                "+" => l + r,
                "-" => l - r,
                "*" => l * r,
                "/" => r == 0 ? 0 : l / r,
                _ => 0
            };
        }

        public override List<int> GetDependencies() => _l.GetDependencies().Concat(_r.GetDependencies()).ToList();
    }
    public class Parser
    {
        private string _text = string.Empty;
        private int _pos;

        public Expression Parse(string text)
        {
            try
            {
                _text = text;
                _pos = 0;
                return ParseAdditive();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Parser] Failed to parse formula: {Formula}", text);
                throw;
            }
        }
        private Expression ParseAdditive()
        {
            Expression left = ParsePrimary();

            while (_pos < _text.Length)
            {
                SkipSpace();
                if (_pos >= _text.Length) break;

                if (_text[_pos] == '+' || _text[_pos] == '-')
                {
                    string op = _text[_pos++].ToString();
                    Expression right = ParsePrimary();
                    left = new BinaryExpression(left, op, right);
                }
                else break;
            }
            return left;
        }
        private Expression ParsePrimary()
        {
            SkipSpace();

            if (_pos >= _text.Length)
            {
                throw new Exception("Unexpected end of expression");
            }

            if (_text[_pos] == '@')
            {
                _pos++;
                return new VariableExpression(ParseInt());
            }

            if (char.IsDigit(_text[_pos]))
                return new LiteralExpression(ParseDecimal());

            if (_text[_pos] == '(')
            {
                _pos++;
                var e = ParseAdditive();
                if (_pos >= _text.Length || _text[_pos] != ')')
                {
                    throw new Exception("Missing closing parenthesis");
                }
                _pos++;
                return e;
            }

            throw new Exception("Parse error");
        }
        private int ParseInt()
        {
            int start = _pos;
            while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
            return int.Parse(_text[start.._pos]);
        }

        private decimal ParseDecimal()
        {
            int start = _pos;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) _pos++;
            return decimal.Parse(_text[start.._pos], CultureInfo.InvariantCulture);
        }

        private void SkipSpace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }
    }
    public class FormulaExecutor
    {
        private const decimal THRESHOLD = 1000;
        private List<FormulaError> _errors = new List<FormulaError>();
        private HashSet<int> _blockedAutoAddIds = new();
        private Dictionary<int, List<int>> _formulaCorrect = new Dictionary<int, List<int>>();
        private Dictionary<int, FormulaDefinition> _parentToFormulas = new Dictionary<int, FormulaDefinition>();
        private Dictionary<int, List<FormulaDefinition>> _childToFormulas = new Dictionary<int, List<FormulaDefinition>>();

        // IF(#StockCode="BVH", @A, @B) – chỉ dùng cho SPECIAL
        private static readonly Regex IF_REGEX = new(@"IF\(#(\w+)=\""(.+?)\""\s*,\s*@(\d+)\s*,\s*@(\w+)\)", RegexOptions.Compiled);
        // IF ĐỔI DẤU (@A * @B < 0) 
        private static readonly Regex IF_SIGN_REGEX = new(@"IF\s*\(\s*\(\s*@(\d+)\s*\*\s*@(\d+)\s*<\s*0\s*\)\s*,\s*-\s*@(\d+)\s*,\s*@(\d+)\s*\)", RegexOptions.IgnoreCase);
        private static string ProcessedKey(FormulaDefinition f) => $"{f.ReportNormID}|{f.Formula}";
        public static List<SheetKeyValue> ExportValue(FinancialReportModel model)
        {
            var mappingValues = new List<SheetKeyValue>();

            try
            {
                var sheetGroups = new List<(List<FinancialReportItem> Rows, string SheetCode)>
                {
                    (model.BalanceSheet, "CD"),
                    (model.IncomeStatement, "KQ"),
                    (model.CashFlow, model.CashFlowMethod?.Equals("indirect", StringComparison.OrdinalIgnoreCase) == true ? "LCGT" : "LCTT"),
                    (model.OffBalanceSheet, "NB")
                };

                foreach (var (rows, sheetCode) in sheetGroups)
                {
                    if (rows == null) continue;

                    foreach (var row in rows)
                    {
                        if (row.Values == null)
                            continue;

                        var item = new SheetKeyValue
                        {
                            ScanIndex = row.ScanIndex,
                            ReportNormID = row.ReportNormID,
                            Item = row.Item,
                            SheetCode = sheetCode,
                            Values = row.Values.ToDictionary(
                                kv => kv.Key,
                                kv => kv.Value.HasValue ? (decimal?)kv.Value.Value : null
                            )
                        };

                        mappingValues.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[ExportValue] FAIL");
            }

            return mappingValues;
        }
        public ResultCheckFormula Execute(List<FormulaDefinition> formulas, FinancialReportModel model, List<ReportDataDetailItem> dbDetailsListQK, string stockCode, int remapAttempt)
        {
            _errors.Clear();
            _blockedAutoAddIds.Clear();
            _formulaCorrect.Clear();
            if (remapAttempt != 1)
            {
                RecheckAutoAddHelper.RecheckAutoAdd(model, formulas);
            }
                
            List<SheetKeyValue> values = ExportValue(model);
            ResultCheckFormula result = new ResultCheckFormula();
            var IDDefaults = result.IDDefaults;

            var systemVars = new Dictionary<string, object>
            {
                ["StockCode"] = stockCode
            };

            try
            {
                var his = new HistoricalMappings();
                if (remapAttempt == 1)
                {
                    // ĐỔI DẤU THEO QUÁ KHỨ
                    //HistoricalMappings.FlipSignIncomeStatementByPast(model, dbDetailsListQK);
                    //XỬ LÝ ĐẶC BIỆT (SPECIAL)
                    ExecuteSpecialFormula(formulas.Where(f => f.SheetCode == "SPECIAL").ToList(), values, systemVars, IDDefaults);
                    MappingRowHelper.AddMissingRowsByReportNorm(model, values);

                    //MAP CHỈ TIÊU AI CHƯA MAP (DÙNG QK)
                    his.MapScanIndexToReportData(model, values, dbDetailsListQK);
                    MappingRowHelper.AddMissingRowsByReportNorm(model, values);

                    //CÔNG THỨC BÌNH THƯỜNG
                    ExecuteScalar(formulas.Where(f => f.SheetCode != "SPECIAL").ToList(), values, IDDefaults, dbDetailsListQK);
                    MappingRowHelper.AddMissingRowsByReportNorm(model, values);

                    result.FormulaErrors.AddRange(_errors);
                    var allIds = _formulaCorrect.SelectMany(kv => new[] { kv.Key }.Concat(kv.Value)).Distinct().ToList();
                    result.IDCorrects.AddRange(allIds);
                    result.IDCorrects.AddRange(IDDefaults);

                    //CHECK QUÁ KHỨ
                    //var id = his.CheckDataPast(model, dbDetailsListQK);
                    //his.GetFormulaID(model, id, formulas, result);
                }
                else
                {
                    //CÔNG THỨC BÌNH THƯỜNG
                    ExecuteScalar(formulas.Where(f => f.SheetCode != "SPECIAL").ToList(), values, IDDefaults, dbDetailsListQK);
                    MappingRowHelper.AddMissingRowsByReportNorm(model, values);

                    result.FormulaErrors.AddRange(_errors);
                    var allIds = _formulaCorrect.SelectMany(kv => new[] { kv.Key }.Concat(kv.Value)).Distinct().ToList();
                    result.IDCorrects.AddRange(allIds);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Formula] Error when executing the formula");
            }
            return result;
        }
        private void ExecuteSpecialFormula(List<FormulaDefinition> formulas, List<SheetKeyValue> values, Dictionary<string, object> systemVars, List<int> IDDefaults)
        {
            var parser = new Parser();

            foreach (var f in formulas.Where(x => x.SheetCode == "SPECIAL"))
            {
                try
                {
                    //TH1: Formula = "null"
                    // VD: "ReportNorm": 4160, "Formula": "null" → Gán tất cả values của item 4160 = null
                    if (string.IsNullOrWhiteSpace(f.Formula) || f.Formula.Equals("null", StringComparison.OrdinalIgnoreCase))
                    {
                        var existed = values.FirstOrDefault(x => x.ReportNormID == f.ReportNormID);
                        if (existed != null)
                        {
                            foreach (var key in existed.Values.Keys.ToList())
                                existed.Values[key] = null;
                        }
                        continue;
                    }

                    string formula = f.Formula.Trim();

                    //TH2: IF với các mã đặc biệt
                    // VD: "ReportNorm": 3700, "Formula": "IF(#StockCode=\"BVH\", @1120, @3700)"
                    var ifMatch = IF_REGEX.Match(formula);
                    if (ifMatch.Success)
                    {
                        string varName = ifMatch.Groups[1].Value;          // StockCode
                        string expected = ifMatch.Groups[2].Value;         // "BVH"
                        int falseId = int.Parse(ifMatch.Groups[3].Value);  // 1120 
                        string truePart = ifMatch.Groups[4].Value;         //3700
                        int? trueId = (truePart.Equals("null", StringComparison.OrdinalIgnoreCase)) ? (int?)null : int.Parse(truePart);

                        var actual = systemVars.TryGetValue(varName, out var v) ? v?.ToString() : null;

                        if (actual == expected)
                        {
                            var items = values.Where(x => x.ReportNormID == trueId && x.ReportNormID != null).ToList();
                            foreach (var item in items)
                            {
                                item.ReportNormID = null;
                            }

                            var itemToRename = values.FirstOrDefault(x => x.ReportNormID == falseId);
                            if (itemToRename != null)
                            {
                                itemToRename.ReportNormID = trueId;
                                if (trueId.HasValue)
                                {
                                    IDDefaults.Add((int)trueId);
                                }
                            }
                        }
                        continue;
                    }

                    //TH3: Copy trực tiếp
                    // VD: "ReportNorm": 3979, "Formula": "@1110" → Đổi ID 3979 thành 1110
                    // VD: "ReportNorm": null, "Formula": "@5524" → Đổi ID 3979 thành null
                    if (Regex.IsMatch(formula, @"^@\d+$"))
                    {
                        int srcId = int.Parse(formula[1..]);
                        var src = values.FirstOrDefault(x => x.ReportNormID == srcId);
                        if (src != null)
                        {
                            if (f.ReportNormID.HasValue)
                            {
                                // TH3.1: ReportNorm có giá trị → đổi srcId thành ReportNormID
                                var exit = values.FirstOrDefault(x => x.ReportNormID == f.ReportNormID.Value);
                                if(exit != null)
                                {
                                    exit.ReportNormID = null;
                                }
                                src.ReportNormID = f.ReportNormID.Value;
                                IDDefaults.Add(f.ReportNormID.Value);
                            }
                            else
                            {
                                // TH3.2: ReportNorm = null → đổi srcId thành null
                                src.ReportNormID = null;
                            }
                        }
                        continue;
                    }

                    //TH4:  IF(@1110, @1125 + @1126 + @1127 + @1128)
                    if (formula.StartsWith("IF(@"))
                    {
                        var ifIds = Regex.Matches(formula, @"@(\d+)").Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();

                        // Bước 1: Dò tất cả ID trong formula có tồn tại trong values không
                        var existingIfItems = ifIds.Select(id => values.FirstOrDefault(x => x.ReportNormID == id)).Where(x => x != null).ToList();

                        // Không có ID nào tồn tại → bỏ qua
                        if (!existingIfItems.Any())
                            continue;

                        int primaryIfId = ifIds.First();
                        var primaryIfItem = existingIfItems.FirstOrDefault(x => x.ReportNormID == primaryIfId);

                        // Bước 2: Có 1110 VÀ có value khác null → 3979 = 1110
                        bool primaryHasValue = primaryIfItem != null && primaryIfItem.Values != null && primaryIfItem.Values.Values.Any(v => v.HasValue);

                        if (primaryHasValue)
                        {
                            var targetIf = values.FirstOrDefault(x => x.ReportNormID == f.ReportNormID);
                            if (targetIf == null)
                            {
                                targetIf = new SheetKeyValue
                                {
                                    ReportNormID = f.ReportNormID,
                                    Values = new Dictionary<string, decimal?>(primaryIfItem.Values),
                                    SheetCode = primaryIfItem.SheetCode
                                };
                                values.Add(targetIf);
                            }
                            else
                            {
                                targetIf.Values = new Dictionary<string, decimal?>(primaryIfItem.Values);
                            }
                            if (f.ReportNormID.HasValue) IDDefaults.Add(f.ReportNormID.Value);
                            continue;
                        }

                        // Bước 3: Không có 1110, hoặc có nhưng value null → kiểm tra children (1125–1128)
                        var childrenIfItems = existingIfItems.Where(x => x.ReportNormID != primaryIfId && x.Values != null).ToList();
                        bool anyChildHasValue = childrenIfItems.Any(c => c.Values.Values.Any(v => v.HasValue));

                        // Tất cả children đều không có value → bỏ qua
                        if (!anyChildHasValue)
                            continue;

                        // Có ít nhất 1 child có value → sum trực tiếp theo từng key
                        var targetIfCalc = values.FirstOrDefault(x => x.ReportNormID == f.ReportNormID);
                        if (targetIfCalc == null)
                        {
                            var firstChild = childrenIfItems.First();
                            targetIfCalc = new SheetKeyValue
                            {
                                ReportNormID = f.ReportNormID,
                                Values = firstChild.Values.Keys.ToDictionary(k => k, _ => (decimal?)null),
                                SheetCode = firstChild.SheetCode
                            };
                            values.Add(targetIfCalc);
                        }

                        foreach (var key in targetIfCalc.Values.Keys.ToList())
                        {
                            decimal sum = 0;
                            bool hasAny = false;
                            foreach (var child in childrenIfItems)
                            {
                                if (child.Values.TryGetValue(key, out var v) && v.HasValue)
                                {
                                    sum += v.Value;
                                    hasAny = true;
                                }
                            }
                            targetIfCalc.Values[key] = hasAny ? sum : null;
                        }

                        if (f.ReportNormID.HasValue) IDDefaults.Add(f.ReportNormID.Value);
                        continue;
                    }

                    //TH5: Công thức tính toán
                    // VD1: "ReportNorm": 4354, "Formula": "@4354 + @1108 + @1109"
                    // VD2: "ReportNorm": 4510, "Formula": "@4510 + @1101"
                    // VD3: "ReportNorm": 4603, "Formula": "@1102 + @1103"

                    var ids = Regex.Matches(formula, @"@\d+").Select(m => int.Parse(m.Value[1..])).Distinct().ToList();
                    if (!ids.Any()) continue;

                    var existingIds = ids.Where(id => values.Any(v => v.ReportNormID == id)).ToList();

                    if (!existingIds.Any())
                    {
                        continue;
                    }

                    var sources = existingIds.Select(id => values.First(v => v.ReportNormID == id)).ToList();

                    SheetKeyValue targetCalc = values.FirstOrDefault(x => x.ReportNormID == f.ReportNormID);
                    if (targetCalc == null)
                    {
                        if (sources.Count == 1)
                        {
                            sources[0].ReportNormID = f.ReportNormID;
                            if (f.ReportNormID != null)
                            {
                                IDDefaults.Add(f.ReportNormID.Value);
                            }
                            continue;
                        }
                        else
                        {
                            var firstChildWithValue = sources.FirstOrDefault(s => s.Values.Values.Any(v => v.HasValue && v.Value != 0));

                            if (firstChildWithValue == null)
                                continue;

                            targetCalc = new SheetKeyValue
                            {
                                ReportNormID = f.ReportNormID,
                                Values = firstChildWithValue.Values.Keys.ToDictionary(k => k, _ => (decimal?)null),
                                SheetCode = firstChildWithValue.SheetCode
                            };
                            values.Add(targetCalc);
                            if (f.ReportNormID != null)
                            {
                                IDDefaults.Add(f.ReportNormID.Value);
                            }
                        }
                        
                    }

                    // Tính sum theo công thức
                    Expression expr = parser.Parse(formula);
                    var childrenIds = expr.GetDependencies().Distinct().ToList();

                    foreach (var key in targetCalc.Values.Keys.ToList())
                    {
                        var ctx = new CalculationContext(new Dictionary<int, decimal?>());
                        bool hasAnyValue = false;

                        foreach (var childId in childrenIds)
                        {
                            var child = values.FirstOrDefault(x => x.ReportNormID == childId);
                            if (child != null && child.Values.TryGetValue(key, out var v) && v.HasValue)
                            {
                                ctx.SetValue(childId, Convert.ToDecimal(v.Value));
                                hasAnyValue = true;
                            }
                        }

                        if (hasAnyValue)
                        {
                            try
                            {
                                decimal result = Convert.ToDecimal(expr.Evaluate(ctx));
                                targetCalc.Values[key] = (decimal)result;
                            }
                            catch
                            {
                                targetCalc.Values[key] = null;
                            }
                        }
                        else
                        {
                            targetCalc.Values[key] = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[SPECIAL][ERROR] ReportNormID={ID} Formula={Formula}", f.ReportNormID, f.Formula);
                }
            }
        }
        private SheetKeyValue EnsureNorm(int? reportNormId, string sheetCode, List<SheetKeyValue> map, List<string> sheetKeys)
        {
            if (!reportNormId.HasValue)
                return null;

            var exits = map.FirstOrDefault(x => x.ReportNormID == reportNormId);
            if (exits != null || _blockedAutoAddIds.Contains(reportNormId.Value))
                return exits;

            var item = new SheetKeyValue
            {
                ReportNormID = reportNormId,
                SheetCode = sheetCode,
                Values = sheetKeys.ToDictionary(k => k, _ => (decimal?)null)
            };
            map.Add(item);
            return item;
        }
        void FlipSignAllKeys(SheetKeyValue item)
        {
            foreach (var k in item.Values.Keys.ToList())
                if (item.Values[k].HasValue)
                    item.Values[k] = -item.Values[k]!.Value;
        }

        bool CheckFormulaAllKeysKQ(Expression expr, SheetKeyValue parent, List<SheetKeyValue> values, List<string> sheetKeys)
        {
            if (expr == null || parent?.Values == null || values == null)
                return false;

            var depIds = expr.GetDependencies()?.ToHashSet();
            if (depIds == null || depIds.Count == 0)
                return false;
            var keysToCheck = sheetKeys.Where((k, index) => index == 0 || index == 2);
            foreach (var key in keysToCheck)
            //foreach (var key in sheetKeys)
            {
                var ctx = new CalculationContext(new Dictionary<int, decimal?>());

                foreach (var c in depIds)
                {
                    var child = values.FirstOrDefault(x => x.ReportNormID == c);
                    if (child?.Values != null && child.Values.TryGetValue(key, out var v) && v.HasValue)
                    {
                        ctx.SetValue(c, Convert.ToDecimal(v.Value));
                    }
                }

                decimal sum = Convert.ToDecimal(expr.Evaluate(ctx));
                decimal parentValue = parent.Values.TryGetValue(key, out var pv) && pv.HasValue ? pv.Value : 0m;

                if (Math.Abs(parentValue - (decimal)sum) > THRESHOLD)
                    return false;
            }

            //chỉ add child thuộc công thức
            if (!parent.ReportNormID.HasValue)
                return false;

            if (!_formulaCorrect.TryGetValue(parent.ReportNormID.Value, out var childrenList))
            {
                childrenList = new List<int>();
                _formulaCorrect[parent.ReportNormID.Value] = childrenList;
            }

            // Add child IDs nếu chưa có
            foreach (var id in values.Where(x => x.ReportNormID.HasValue && depIds.Contains(x.ReportNormID.Value)).Select(x => x.ReportNormID.Value))
            {
                if (!childrenList.Contains(id))
                    childrenList.Add(id);
            }

            // Đảm bảo parent cũng nằm trong danh sách
            if (!childrenList.Contains(parent.ReportNormID.Value))
                childrenList.Add(parent.ReportNormID.Value);

            return true;
        }
        bool CheckValuePast(Dictionary<string, decimal?> parentValues, List<string> sheetKeys, List<ReportDataDetailItem> parentPasts)
        {
            if (parentValues == null || sheetKeys == null)
                return false;

            if (parentPasts == null || parentPasts.Count == 0)
            {
                foreach (var key in sheetKeys)
                {
                    decimal parentValue = parentValues.TryGetValue(key, out var pv) && pv.HasValue ? pv.Value : 0m;
                    if (parentValue == 0)
                        return true;
                }
                return false;
            }

            // Check parent có trùng dữ liệu quá khứ không
            foreach (var key in sheetKeys)
            {
                decimal parentValue = parentValues.TryGetValue(key, out var pv) && pv.HasValue ? pv.Value : 0m;
                foreach(var parentPast in parentPasts)
                {
                    if (parentPast?.Value.HasValue == true && parentValue == parentPast.Value.Value)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        private void AddChildRecursive(SheetKeyValue currentNode, HashSet<int> depIds, List<SheetKeyValue> values, int depth = 0)
        {
            if (depth > 20) return; // chống vòng lặp vô hạn

            if (currentNode?.Values == null || !currentNode.Values.Any(x => x.Value.HasValue && x.Value.Value != 0))
                return;

            var firstChildId = depIds.First();

            // Nếu child đã tồn tại rồi thì không add nữa
            var existingChild = values.FirstOrDefault(x => x.ReportNormID == firstChildId);
            if (existingChild != null)
                return;

            // Tạo child mới với value = currentNode
            var newChild = new SheetKeyValue
            {
                ReportNormID = firstChildId,
                Values = new Dictionary<string, decimal?>(currentNode.Values),
                SheetCode = currentNode.SheetCode
            };
            values.Add(newChild);
            var logValues = string.Join(", ", currentNode.Values.Select(x => $"{x.Key}={x.Value}"));
            Log.Information("[FORMULA][AddChild] ParentID={ParentID}, ChildID={ChildID} -> {Value}, Depth={Depth}", currentNode.ReportNormID, firstChildId, logValues, depth);

            // Nếu child này cũng có công thức riêng → đệ quy tiếp
            if (_parentToFormulas.TryGetValue(firstChildId, out var childFormula) && !string.IsNullOrEmpty(childFormula.Formula))
            {
                try
                {
                    var parser = new Parser();
                    var childExpr = parser.Parse(childFormula.Formula);
                    var childDepIds = childExpr.GetDependencies()?.ToHashSet();
                    if (childDepIds != null && childDepIds.Count > 0)
                    {
                        AddChildRecursive(newChild, childDepIds, values, depth + 1);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[FORMULA][AddChildRecursive] Parse lỗi cho ChildID={ChildID} Formula={Formula}", firstChildId, childFormula.Formula);
                }
            }
        }
        bool CheckFormulaAllKeys(Expression expr, SheetKeyValue parent,  List<SheetKeyValue> values, List<string> sheetKeys, List<ReportDataDetailItem> parentPast)
        {
            try
            {
                if (expr == null || parent == null || parent.Values == null || values == null)
                    return false;

                var depIds = expr.GetDependencies();
                if (depIds == null)
                    return false;

                var depSet = new HashSet<int>(depIds);
                if (depSet.Count == 0)
                    return false;

                bool hasChild = false;
                bool primaryOk = true;

                bool isTruePast = CheckValuePast(parent.Values, sheetKeys, parentPast);

                // STEP 1: CHECK KEY 0 & 2
                for (int i = 0; i < sheetKeys.Count; i++)
                {
                    if (i != 0 && i != 2)
                        continue;

                    string key = sheetKeys[i];

                    try
                    {
                        var ctx = new CalculationContext(new Dictionary<int, decimal?>());

                        foreach (var depId in depSet)
                        {
                            var child = values.FirstOrDefault(x => x.ReportNormID == depId);

                            if (child != null)
                            {
                                hasChild = true;

                                if (child.Values != null && child.Values.TryGetValue(key, out var v) && v.HasValue)
                                {
                                    ctx.SetValue(depId, Convert.ToDecimal(v.Value));
                                }
                            }
                        }

                        decimal sum = Convert.ToDecimal(expr.Evaluate(ctx) ?? 0m);

                        decimal parentValue = 0m;
                        if (parent.Values.TryGetValue(key, out var pv) && pv.HasValue)
                            parentValue = pv.Value;

                        if (Math.Abs(parentValue - sum) > THRESHOLD)
                        {
                            primaryOk = false;
                            break;
                        }
                    }
                    catch (Exception exKey)
                    {
                        Log.Error(exKey, "[FORMULA][EVAL_ERROR] ParentID={ParentID} Key={Key}", parent.ReportNormID, key);
                        primaryOk = false;
                        break;
                    }
                }

                //1. Công thức đúng, quá khứ đúng or sai
                if (primaryOk) 
                {
                    if (!parent.ReportNormID.HasValue)
                        return true;

                    if (!_formulaCorrect.TryGetValue(parent.ReportNormID.Value, out var list))
                    {
                        list = new List<int>();
                        _formulaCorrect[parent.ReportNormID.Value] = list;
                    }

                    foreach (var id in depSet)
                    {
                        if (values.Any(x => x.ReportNormID == id))
                        {
                            if (!list.Contains(id))
                                list.Add(id);
                        }
                    }

                    if (!list.Contains(parent.ReportNormID.Value))
                        list.Add(parent.ReportNormID.Value);

                    return true;
                }

                var parentItem = values.FirstOrDefault(x => x.ReportNormID == parent.ReportNormID);

                //2.  Công thức sai, quá khứ đúng
                if (isTruePast) 
                {
                    if (!hasChild)
                    {
                        AddChildRecursive(parent, depSet, values);
                        return true;
                    }
                    return false;
                }


                //3. Công thức sai, quá khứ sai
                if (parentItem == null || parentItem.Values == null)
                    return false;

                bool isUpdated = false;

                var updatedValues = new Dictionary<string, decimal?>();

                foreach (var key in sheetKeys)
                {
                    try
                    {
                        var ctx = new CalculationContext(new Dictionary<int, decimal?>());

                        foreach (var depId in depSet)
                        {
                            var child = values.FirstOrDefault(x => x.ReportNormID == depId);

                            if (child != null)
                            {
                                hasChild = true;

                                if (child.Values != null && child.Values.TryGetValue(key, out var v) && v.HasValue)
                                {
                                    ctx.SetValue(depId, Convert.ToDecimal(v.Value));
                                }
                            }
                        }

                        decimal sum = Convert.ToDecimal(expr.Evaluate(ctx) ?? 0m);

                        updatedValues[key] = sum;
                        isUpdated = true;
                    }
                    catch (Exception exKey)
                    {
                        Log.Error(exKey, "[FORMULA][EVAL_ERROR] ParentID={ParentID} Key={Key} Formula={Formula}", parent.ReportNormID, key, expr.ToString());
                    }
                }

                if (isUpdated)
                {
                    bool pastValid = CheckValuePast(updatedValues, sheetKeys, parentPast);

                    if (pastValid)
                    {
                        foreach (var kv in updatedValues)
                            parentItem.Values[kv.Key] = kv.Value;

                        var logValues = string.Join(", ", parentItem.Values.Select(x => $"{x.Key}={x.Value}"));
                        Log.Information("[FORMULA][UPDATE_PARENT] ParentID={ParentID} Values={Values}", parentItem.ReportNormID, logValues);

                        if (!hasChild)
                            AddChildRecursive(parent, depSet, values);

                        return true;
                    }
                    else
                    {
                        string targetKey = null;

                        if (parentPast != null && parentPast.Count == 2)
                        {
                            var past = parentPast.FirstOrDefault();

                            if (past != null)
                            {
                                if (past.IsCumulative == 0 && sheetKeys.Count <= 2)
                                    targetKey = sheetKeys[1];

                                else if (past.IsCumulative == 1 && sheetKeys.Count > 3)
                                    targetKey = sheetKeys[3];
                            }
                        }
                        else if (parentPast != null && parentPast.Count == 1 && sheetKeys.Count > 1)
                        {
                            targetKey = sheetKeys[1];
                        }

                        if (targetKey != null)
                        {
                            var pastValue = parentPast.FirstOrDefault()?.Value;

                            parentItem.Values[targetKey] = pastValue;
                            parentItem.IsUpdatePast = true;

                            Log.Information("[FORMULA][UPDATE_PARENTPAST] ParentID={ParentID} Key={Key} Value={Value}", parentItem.ReportNormID, targetKey, pastValue);
                        }

                        return false;
                    }
                }

                // ADD FORMULA CORRECT
                if (!parent.ReportNormID.HasValue)
                    return false;

                if (!_formulaCorrect.TryGetValue(parent.ReportNormID.Value, out var childrenList))
                {
                    childrenList = new List<int>();
                    _formulaCorrect[parent.ReportNormID.Value] = childrenList;
                }

                foreach (var id in depSet)
                {
                    if (values.Any(x => x.ReportNormID == id))
                    {
                        if (!childrenList.Contains(id))
                            childrenList.Add(id);
                    }
                }

                if (!childrenList.Contains(parent.ReportNormID.Value))
                    childrenList.Add(parent.ReportNormID.Value);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FORMULA][CHECK_ERROR] ParentID={ParentID} Formula={Formula}", parent?.ReportNormID, expr?.ToString());
                return false;
            }
        }
        private void ExecuteScalar(List<FormulaDefinition> formulas, List<SheetKeyValue> values, List<int> IDDefaults, List<ReportDataDetailItem> dbDetailsListQK)
        {
            var globalProcessed = new HashSet<string>(); // Các formula đã xử lý xong
            //var globalFlipped = new HashSet<int>();    // Các item đã flip dấu (không flip lại nữa)
            var parser = new Parser();

            foreach (var f in formulas)
            {
                try
                {
                    List<int> children;
                    var signMatch = IF_SIGN_REGEX.Match(f.Formula);
                    if (signMatch.Success)
                    {
                        children = new List<int> { int.Parse(signMatch.Groups[1].Value), int.Parse(signMatch.Groups[2].Value) };
                    }
                    else
                    {
                        var expr = parser.Parse(f.Formula);
                        children = expr.GetDependencies().Distinct().ToList();
                    }
                    if (f.ReportNormID.HasValue)
                    {
                        _parentToFormulas[f.ReportNormID.Value] = f;
                        foreach (var childId in children)
                        {
                            if (!_childToFormulas.ContainsKey(childId))
                                _childToFormulas[childId] = new List<FormulaDefinition>();
                            _childToFormulas[childId].Add(f);
                        }
                    }
                }
                catch { }
            }
            ProcessFormulasRecursive(formulas, values, globalProcessed, _childToFormulas, _parentToFormulas, 0, IDDefaults, dbDetailsListQK);
        }
        private void ProcessFormulasRecursive(List<FormulaDefinition> formulas, List<SheetKeyValue> values, HashSet<string> globalProcessed, Dictionary<int, List<FormulaDefinition>> childToFormulas, Dictionary<int, FormulaDefinition> parentToFormulas, int depth, List<int> IDDefaults, List<ReportDataDetailItem> dbDetailsListQK)
        {
            // Safety check: tránh đệ quy vô hạn
            if (depth > 100)
            {
                Log.Error("[CHECKFORMULA] Recursive depth > 100");
                return;
            }

            var parser = new Parser();
            //var sheetKeyMap = values.GroupBy(v => v.SheetCode).ToDictionary(g => g.Key, g => g.SelectMany(x => x.Values.Keys).Distinct().ToList());
            var sheetKeyMap = values.GroupBy(v => v.SheetCode).ToDictionary(g => g.Key, g => g.SelectMany(x => x.Values).Where(kv => kv.Value.HasValue && kv.Value.Value != 0).Select(kv => kv.Key).Distinct().ToList());

            foreach (var formula in formulas)
            {  
                //Lấy value quá khứ 
                var parentPast = dbDetailsListQK.Where(x => x.ReportNormID == formula.ReportNormID).ToList();

                if (!IF_SIGN_REGEX.IsMatch(formula.Formula))
                {
                    if (globalProcessed.Contains(ProcessedKey(formula)))
                        continue;
                }

                try
                {
                    var matchedKey = sheetKeyMap.Keys.FirstOrDefault(key => formula.SheetCode.Contains(key));
                    if (string.IsNullOrEmpty(matchedKey) || !sheetKeyMap.TryGetValue(matchedKey, out var sheetKeys) || sheetKeys.Count == 0)
                        continue;

                    //Xử lí IF(ĐB): ("ReportNorm": 2223, "Formula": "IF((@2222 * @2223 < 0), -@2223, @2223)")
                    var signMatch = IF_SIGN_REGEX.Match(formula.Formula);
                    if (signMatch.Success)
                    {
                        int aId = int.Parse(signMatch.Groups[1].Value);
                        int bId = int.Parse(signMatch.Groups[2].Value);
                        var aItem = values.FirstOrDefault(x => x.ReportNormID == aId);
                        var bItem = values.FirstOrDefault(x => x.ReportNormID == bId);
                        if (aItem == null || bItem == null) continue;

                        var target = values.FirstOrDefault(x => x.ReportNormID == formula.ReportNormID);
                        if (target == null)
                        {
                            target = new SheetKeyValue
                            {
                                ReportNormID = formula.ReportNormID,
                                SheetCode = formula.SheetCode,
                                Values = bItem.Values.Keys.ToDictionary(k => k, _ => (decimal?)null)
                            };
                            values.Add(target);
                        }

                        foreach (var key in target.Values.Keys.ToList())
                        {
                            decimal aVal = aItem.Values.TryGetValue(key, out var av) && av.HasValue ? av.Value : 0;
                            decimal bVal = bItem.Values.TryGetValue(key, out var bv) && bv.HasValue ? bv.Value : 0;
                            target.Values[key] = (aVal * bVal < 0) ? -bVal : bVal;
                        }

                        globalProcessed.Add(ProcessedKey(formula));
                        continue;
                    }

                    //Xử lí công thức bình thường
                    if (!formula.ReportNormID.HasValue)
                        continue;

                    Expression expr = parser.Parse(formula.Formula);
                    var children = expr.GetDependencies().Distinct().ToList();

                    var parent = EnsureNorm(formula.ReportNormID, formula.SheetCode, values, sheetKeys);

                    bool parentExists = parent != null && parent.Values.Any(v => v.Value.HasValue && v.Value.Value != 0);
                    bool anyChildExists = children.Any(c => values.Any(x => x.ReportNormID == c));

                    //RULE 0: Xóa parent nếu parent và child k tồn tại
                    if (!parentExists && !anyChildExists)
                    {
                        values.RemoveAll(x => x.ReportNormID == formula.ReportNormID.Value);
                        continue;
                    }

                    //RULE 1: Parent trống -> tính từ children
                    if (!parentExists && !_blockedAutoAddIds.Contains(formula.ReportNormID.Value) && children.Any(id => !_blockedAutoAddIds.Contains(id)))
                    {
                        bool hasAnyKeySum = false;
                        foreach (var key in sheetKeys)
                        {
                            var ctx = new CalculationContext(new Dictionary<int, decimal?>());
                            foreach (var c in children)
                            {
                                var ci = values.FirstOrDefault(x => x.ReportNormID == c);
                                if (ci != null && ci.Values.TryGetValue(key, out var v) && v.HasValue)
                                    ctx.SetValue(c, Convert.ToDecimal(v.Value));
                            }
                            decimal sum = Convert.ToDecimal(expr.Evaluate(ctx));
                            if (sum != 0)
                            {
                                parent.Values[key] = (decimal)sum;
                                hasAnyKeySum = true;
                            }
                        }
                        if (parent.Values != null && parent.Values.Values.Any(v => v.HasValue))
                        {
                            //IDDefaults.Add(parent.ReportNormID.Value);
                            Log.Information("CHECKFORMULA: [AddParent] -> ReportNormID: {ReportNormID}, Values: {Values}", parent.ReportNormID, parent.Values);
                        }
                        if (hasAnyKeySum)
                        {
                            globalProcessed.Add(ProcessedKey(formula));
                            continue;
                        }
                    }

                    //RULE 3.1: Check công thức đã đúng chưa
                    if (CheckFormulaAllKeys(expr, parent, values, sheetKeys, parentPast))
                    {
                        globalProcessed.Add(ProcessedKey(formula));
                        continue;
                    }

                    //RULE 3.2: Công thức sai thay đổi dấu children để fix công thức (chỉ với sheet KQ)
                    if (formula.SheetCode?.Contains("KQ") == true && !CheckFormulaAllKeys(expr, parent, values, sheetKeys, parentPast))
                    {
                        bool fixedBySign = false;

                        // Với bảng KQ: resolve các child item=null sang IDs lá thực sự có value (đệ quy)
                        // VD: x = a + b + d, a = b1+b2 (null), b = b1+b2 (null)
                        //     resolvedFlipIds = {b1, b2, d}
                        //     Sau khi flip b1: rebuild bottom-up: b=b1_flip+b2, a=b_updated+c, rồi CheckFormula
                        // Dùng ResolveFlipIds cho tất cả children:
                        // - child có Item -> trả về {childId}
                        // - child không có Item -> expand đệ quy qua công thức của nó
                        var resolvedFlipIds = new List<int>();
                        foreach (var childId in children)
                        {
                            var leafIds = ResolveFlipIds(childId, values);
                            foreach (var leafId in leafIds)
                                if (!resolvedFlipIds.Contains(leafId))
                                    resolvedFlipIds.Add(leafId);
                        }

                        //Chỉ Flip dấu cho trường hợp id có value
                        var childrenWithValues = resolvedFlipIds.Where(childId =>
                        {
                            var childItem = values.FirstOrDefault(x => x.ReportNormID == childId);
                            if (childItem?.Values == null) return false;
                            return childItem.Values.Values.Any(v => v.HasValue && v.Value != 0);
                        }).ToList();

                        if (childrenWithValues.Count == 0)
                        {
                            LogSaiSo(formulas, formula, expr, values, children);
                            globalProcessed.Add(ProcessedKey(formula));
                            continue;
                        }
                        var target = new HashSet<int> { 3213, 3214, 5348, 5338 };
                        // Thay đổi dấu theo tổ hợp các con (dùng resolvedFlipIds thay cho children gốc)
                        foreach (var combo in GenerateCombinations(resolvedFlipIds))
                        {
                            if (combo.Count == target.Count && !combo.Except(target).Any())
                            {
                                Log.Information("combo đúng là: 3213,3214,5348,5338");
                            }
                            // Tìm tất cả IDs trung gian cần rebuild (tổ tiên của các lá bị flip)
                            // VD: flip b1 -> cần rebuild b (b=b1+b2), rồi rebuild a (a=b+c)
                            var rebuildIds = CollectAncestorsBottomUp(combo, children, _parentToFormulas, parser);

                            // Backup: lá bị flip + tất cả tầng trung gian cần rebuild
                            var backupIds = combo.Concat(rebuildIds).Distinct().ToList();
                            var backup = values
                                .Where(x => x.ReportNormID.HasValue && backupIds.Contains(x.ReportNormID.Value))
                                .ToDictionary(x => x.ReportNormID.Value, x => new Dictionary<string, decimal?>(x.Values));

                            // Flip dấu các lá trong combo
                            foreach (var id in combo)
                            {
                                var item = values.FirstOrDefault(x => x.ReportNormID == id);
                                if (item != null) FlipSignAllKeys(item);
                            }

                            // Rebuild bottom-up: tính lại từng tầng trung gian theo thứ tự từ lá lên gốc
                            // VD: b1 flip -> b = b1_flip + b2 -> a = b_updated + c
                            RebuildBottomUp(rebuildIds, values, parser);

                            // Check xem flip này có fix được công thức không
                            if (CheckFormulaAllKeys(expr, parent, values, sheetKeys, parentPast))
                            {
                                //Log.Information("[FLIP] Formula {Parent} flipped items: {Items} (depth={Depth})", formula.ReportNormID, string.Join(",", combo), depth);

                                var affectedFormulas = new HashSet<FormulaDefinition>();

                                foreach (var flippedId in combo)
                                {
                                    // Type 1: flippedId là child của formula af -> tính lại sum các con -> cập nhật thẳng giá trị parent của af
                                    // Sau khi cập nhật, check lại formula của af: nếu sai thì rollback giá trị cha về cũ
                                    if (childToFormulas.TryGetValue(flippedId, out var directFormulas))
                                    {
                                        foreach (var af in directFormulas)
                                        {
                                            if (IF_SIGN_REGEX.IsMatch(af.Formula) || !af.ReportNormID.HasValue)
                                                continue;

                                            try
                                            {
                                                var afExpr = parser.Parse(af.Formula);
                                                var afParent = values.FirstOrDefault(x => x.ReportNormID == af.ReportNormID);
                                                if (afParent == null) continue;

                                                var afChildren = afExpr.GetDependencies().Distinct().ToList();
                                                var matchedAfKey = sheetKeyMap.Keys.FirstOrDefault(k => af.SheetCode.Contains(k));
                                                if (string.IsNullOrEmpty(matchedAfKey)) continue;
                                                var afSheetKeys = sheetKeyMap[matchedAfKey];

                                                // Backup giá trị cha trước khi cập nhật
                                                var afParentBackup = new Dictionary<string, decimal?>(afParent.Values);

                                                foreach (var key in afSheetKeys)
                                                {
                                                    var ctx = new CalculationContext(new Dictionary<int, decimal?>());
                                                    foreach (var cId in afChildren)
                                                    {
                                                        var ci = values.FirstOrDefault(x => x.ReportNormID == cId);
                                                        if (ci != null && ci.Values.TryGetValue(key, out var v) && v.HasValue)
                                                            ctx.SetValue(cId, Convert.ToDecimal(v.Value));
                                                    }
                                                    decimal sum = Convert.ToDecimal(afExpr.Evaluate(ctx));
                                                    if (sum != 0)
                                                        afParent.Values[key] = sum;
                                                }

                                                // Check lại formula của cha sau khi cập nhật
                                                bool afStillValid = false;
                                                try
                                                {
                                                    if (sheetKeyMap.TryGetValue(matchedAfKey, out var afKeys2))
                                                        afStillValid = CheckFormulaAllKeys(afExpr, afParent, values, afKeys2, parentPast);
                                                }
                                                catch { afStillValid = true; }

                                                if (afStillValid)
                                                {
                                                    globalProcessed.Remove(ProcessedKey(af));
                                                    Log.Information("[TYPE1] Cập nhật parent ID={Parent} từ con sau flip child {Child}", af.ReportNormID, flippedId);
                                                }
                                                else
                                                {
                                                    // Rollback giá trị cha về cũ nếu formula không hợp lệ
                                                    afParent.Values = afParentBackup;
                                                    Log.Warning("[TYPE1] Rollback parent ID={Parent} vì CheckFormulaAllKeys sai sau khi cập nhật từ flip child {Child}", af.ReportNormID, flippedId);
                                                }
                                            }
                                            catch (Exception exAf)
                                            {
                                                Log.Error(exAf, "[TYPE1][ERROR] af={ID}", af.ReportNormID);
                                            }
                                        }
                                    }

                                    // Type 2: Formula mà flippedId là parent (công thức tạo ra flippedId) -> VD: flip a trong "d = a + e" -> cần check lại "a = b + c"
                                    if (parentToFormulas.TryGetValue(flippedId, out var parentFormula))
                                    {
                                        // Check xem formula này còn đúng không
                                        bool stillValid = false;
                                        try
                                        {
                                            var signMatch2 = IF_SIGN_REGEX.Match(parentFormula.Formula);
                                            if (signMatch2.Success)
                                            {
                                                stillValid = true;
                                            }
                                            else
                                            {
                                                var expr2 = parser.Parse(parentFormula.Formula);
                                                var parent2 = values.FirstOrDefault(x => x.ReportNormID == parentFormula.ReportNormID);
                                                if (parent2 != null && sheetKeyMap.TryGetValue(parentFormula.SheetCode, out var keys2))
                                                {
                                                    stillValid = CheckFormulaAllKeys(expr2, parent2, values, keys2, parentPast);
                                                }
                                            }
                                        }
                                        catch { stillValid = true; }

                                        // Nếu công thức sai -> remove khỏi processed để xử lý lại
                                        if (!stillValid)
                                        {
                                            globalProcessed.Remove(ProcessedKey(parentFormula));
                                            affectedFormulas.Add(parentFormula);

                                            //Log.Information("[CASCADE] Recheck formula {Parent} vì child {Child} đã flip", parentFormula.ReportNormID, flippedId);
                                        }
                                    }
                                }

                                // Xử lý đệ quy chỉ cho Type 2
                                if (affectedFormulas.Count > 0)
                                {
                                    ProcessFormulasRecursive(affectedFormulas.ToList(), values, globalProcessed, childToFormulas, parentToFormulas, depth + 1, IDDefaults, dbDetailsListQK);
                                }

                                fixedBySign = true;
                                break;
                            }

                            // Rollback nếu không fix được
                            foreach (var b in backup)
                            {
                                var item = values.First(x => x.ReportNormID == b.Key);
                                item.Values = new Dictionary<string, decimal?>(b.Value);
                            }
                        }
                        //if (!fixedBySign)
                        //{
                        //    LogSaiSo(formulas, formula, expr, values, children);
                        //}
                        globalProcessed.Add(ProcessedKey(formula));
                    }
                    //if (formula.SheetCode?.Contains("KQ") == true && !CheckFormulaAllKeys(expr, parent, values, sheetKeys, parentPast))
                    //{
                    //    bool fixedBySign = false;

                    //    //Chỉ Flip dấu cho trường hợp id có value
                    //    var childrenWithValues = children.Where(childId =>
                    //    {
                    //        var childItem = values.FirstOrDefault(x => x.ReportNormID == childId);
                    //        if (childItem?.Values == null) return false;

                    //        return childItem.Values.Values.Any(v => v.HasValue && v.Value != 0);
                    //    }).ToList();

                    //    if (childrenWithValues.Count == 0)
                    //    {
                    //        LogSaiSo(formulas, formula, expr, values, children);
                    //        globalProcessed.Add(ProcessedKey(formula));
                    //        continue;
                    //    }
                    //    // Thay đổi dấu theo tổ hợp các con
                    //    foreach (var combo in GenerateCombinations(children))
                    //    {
                    //        var backup = values.Where(x => x.ReportNormID.HasValue && combo.Contains(x.ReportNormID.Value)).ToDictionary(x => x.ReportNormID.Value, x => new Dictionary<string, decimal?>(x.Values));

                    //        // Flip dấu
                    //        foreach (var id in combo)
                    //        {
                    //            var item = values.FirstOrDefault(x => x.ReportNormID == id);
                    //            if (item != null) FlipSignAllKeys(item);
                    //        }

                    //        // Check xem flip này có fix được công thức không
                    //        if (CheckFormulaAllKeys(expr, parent, values, sheetKeys, parentPast))
                    //        {
                    //            //Log.Information("[FLIP] Formula {Parent} flipped items: {Items} (depth={Depth})", formula.ReportNormID, string.Join(",", combo), depth);

                    //            var affectedFormulas = new HashSet<FormulaDefinition>();

                    //            foreach (var flippedId in combo)
                    //            {
                    //                // Type 1: flippedId là child của formula af -> tính lại sum các con -> cập nhật thẳng giá trị parent của af
                    //                if (childToFormulas.TryGetValue(flippedId, out var directFormulas))
                    //                {
                    //                    foreach (var af in directFormulas)
                    //                    {
                    //                        if (IF_SIGN_REGEX.IsMatch(af.Formula) || !af.ReportNormID.HasValue)
                    //                            continue;

                    //                        try
                    //                        {
                    //                            var afExpr = parser.Parse(af.Formula);
                    //                            var afParent = values.FirstOrDefault(x => x.ReportNormID == af.ReportNormID);
                    //                            if (afParent == null) continue;

                    //                            var afChildren = afExpr.GetDependencies().Distinct().ToList();
                    //                            var matchedAfKey = sheetKeyMap.Keys.FirstOrDefault(k => af.SheetCode.Contains(k));
                    //                            if (string.IsNullOrEmpty(matchedAfKey)) continue;
                    //                            var afSheetKeys = sheetKeyMap[matchedAfKey];

                    //                            foreach (var key in afSheetKeys)
                    //                            {
                    //                                var ctx = new CalculationContext(new Dictionary<int, decimal?>());
                    //                                foreach (var cId in afChildren)
                    //                                {
                    //                                    var ci = values.FirstOrDefault(x => x.ReportNormID == cId);
                    //                                    if (ci != null && ci.Values.TryGetValue(key, out var v) && v.HasValue)
                    //                                        ctx.SetValue(cId, Convert.ToDecimal(v.Value));
                    //                                }
                    //                                decimal sum = Convert.ToDecimal(afExpr.Evaluate(ctx));
                    //                                if (sum != 0)
                    //                                    afParent.Values[key] = sum;
                    //                            }

                    //                            globalProcessed.Remove(ProcessedKey(af));
                    //                            //Log.Information("[TYPE1] Cập nhật parent ID={Parent} từ con sau flip child {Child}", af.ReportNormID, flippedId);
                    //                        }
                    //                        catch (Exception exAf)
                    //                        {
                    //                            Log.Error(exAf, "[TYPE1][ERROR] af={ID}", af.ReportNormID);
                    //                        }
                    //                    }
                    //                }

                    //                // Type 2: Formula mà flippedId là parent (công thức tạo ra flippedId) -> VD: flip a trong "d = a + e" -> cần check lại "a = b + c"
                    //                if (parentToFormulas.TryGetValue(flippedId, out var parentFormula))
                    //                {
                    //                    // Check xem formula này còn đúng không
                    //                    bool stillValid = false;
                    //                    try
                    //                    {
                    //                        var signMatch2 = IF_SIGN_REGEX.Match(parentFormula.Formula);
                    //                        if (signMatch2.Success)
                    //                        {
                    //                            stillValid = true;
                    //                        }
                    //                        else
                    //                        {
                    //                            var expr2 = parser.Parse(parentFormula.Formula);
                    //                            var parent2 = values.FirstOrDefault(x => x.ReportNormID == parentFormula.ReportNormID);
                    //                            if (parent2 != null && sheetKeyMap.TryGetValue(parentFormula.SheetCode, out var keys2))
                    //                            {
                    //                                stillValid = CheckFormulaAllKeys(expr2, parent2, values, keys2, parentPast);
                    //                            }
                    //                        }
                    //                    }
                    //                    catch { stillValid = true; }

                    //                    // Nếu công thức sai -> remove khỏi processed để xử lý lại
                    //                    if (!stillValid)
                    //                    {
                    //                        globalProcessed.Remove(ProcessedKey(parentFormula));
                    //                        affectedFormulas.Add(parentFormula);

                    //                        //Log.Information("[CASCADE] Recheck formula {Parent} vì child {Child} đã flip", parentFormula.ReportNormID, flippedId);
                    //                    }
                    //                }
                    //            }

                    //            // Xử lý đệ quy chỉ cho Type 2
                    //            if (affectedFormulas.Count > 0)
                    //            {
                    //                ProcessFormulasRecursive(affectedFormulas.ToList(), values, globalProcessed, childToFormulas, parentToFormulas, depth + 1, IDDefaults, dbDetailsListQK);
                    //            }

                    //            fixedBySign = true;
                    //            break;
                    //        }

                    //        // Rollback nếu không fix được
                    //        foreach (var b in backup)
                    //        {
                    //            var item = values.First(x => x.ReportNormID == b.Key);
                    //            item.Values = new Dictionary<string, decimal?>(b.Value);
                    //        }
                    //    }
                    //    //if (!fixedBySign)
                    //    //{
                    //    //    LogSaiSo(formulas, formula, expr, values, children);
                    //    //}
                    //    globalProcessed.Add(ProcessedKey(formula));
                    //}

                    if (CheckFormulaAllKeys(expr, parent, values, sheetKeys, parentPast)) continue;

                    LogSaiSo(formulas, formula, expr, values, children);
                    globalProcessed.Add(ProcessedKey(formula));
                }
                catch (Exception ex)
                {
                    Log.Error("[FORMULA][ERROR]: {ex} Sheet={Sheet} ID={ID}", ex, formula.SheetCode, formula.ReportNormID);
                    globalProcessed.Add(ProcessedKey(formula));
                }
            }
        }
        private List<int> ResolveFlipIds(int childId, List<SheetKeyValue> values, HashSet<int> visited = null)
        {
            visited ??= new HashSet<int>();
            if (!visited.Add(childId)) // chống vòng lặp vô hạn
                return new List<int>();

            var childItem = values.FirstOrDefault(x => x.ReportNormID == childId);
            if (childItem == null) return new List<int>();

            bool hasItem = !string.IsNullOrEmpty(childItem?.Item);

            if (hasItem)
                return new List<int> { childId };

            // Không có value -> thử mở rộng qua _parentToFormulas (childId là cha của một công thức)
            if (_parentToFormulas.TryGetValue(childId, out var ownFormula) && !string.IsNullOrEmpty(ownFormula?.Formula))
            {
                try
                {
                    var parser = new Parser();
                    var subExpr = parser.Parse(ownFormula.Formula);
                    var subChildren = subExpr.GetDependencies().Distinct().ToList();

                    var result = new List<int>();
                    foreach (var subId in subChildren)
                    {
                        var resolved = ResolveFlipIds(subId, values, visited);
                        foreach (var r in resolved)
                            if (!result.Contains(r)) result.Add(r);
                    }

                    if (result.Count > 0)
                    {
                        //Log.Information("[KQ][ResolveFlip] ID={ChildID} null/empty -> expand qua công thức [{Formula}] -> [{Resolved}]", childId, ownFormula.Formula, string.Join(",", result));
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[KQ][ResolveFlip] Lỗi parse công thức của ID={ChildID}", childId);
                }
            }

            //Log.Debug("[KQ][ResolveFlip] ID={ChildID} null/empty và không tìm được ID thay thế, bỏ qua", childId);
            return new List<int>();
        }

        private List<int> CollectAncestorsBottomUp(List<int> flippedLeaves, List<int> formulaChildren, Dictionary<int, FormulaDefinition> parentToFormulas, Parser parser)
        {
            // Build childToParent map bằng cách duyệt đệ quy từ formulaChildren xuống
            // childToParent[x] = y nghĩa là "x là con trực tiếp của y trong cây công thức"
            var childToParent = new Dictionary<int, int>();

            void BuildTree(int nodeId, HashSet<int> vis)
            {
                if (!vis.Add(nodeId)) return;
                if (!parentToFormulas.TryGetValue(nodeId, out var f) || string.IsNullOrEmpty(f?.Formula)) return;
                try
                {
                    foreach (var dep in parser.Parse(f.Formula).GetDependencies().Distinct())
                    {
                        childToParent[dep] = nodeId;
                        BuildTree(dep, vis);
                    }
                }
                catch { }
            }

            var visited = new HashSet<int>();
            foreach (var c in formulaChildren)
                BuildTree(c, visited);

            // Với mỗi lá bị flip, leo lên childToParent để thu thập các tổ tiên cần rebuild
            var ancestors = new List<int>();
            foreach (var leaf in flippedLeaves)
            {
                var cur = leaf;
                while (childToParent.TryGetValue(cur, out var par))
                {
                    if (!ancestors.Contains(par))
                        ancestors.Add(par);
                    cur = par;
                }
            }

            // Topological sort bottom-up:
            // Chọn node mà không có node nào khác trong remaining đang là con của nó
            // (tức node gần lá nhất, chưa có dependency nào chờ tính trước nó)
            var sorted = new List<int>();
            var remaining = new HashSet<int>(ancestors);
            int maxIter = ancestors.Count * 2 + 2;
            while (remaining.Count > 0 && maxIter-- > 0)
            {
                // Node n có thể tính được khi tất cả các con của n (trong remaining) đã được xử lý
                // Tức là: không còn node nào trong remaining mà childToParent[x] == n
                var batch = remaining
                    .Where(n => !remaining.Any(other => childToParent.TryGetValue(other, out var p) && p == n))
                    .ToList();
                if (batch.Count == 0) batch = remaining.ToList(); // fallback tránh treo
                foreach (var n in batch)
                {
                    sorted.Add(n);
                    remaining.Remove(n);
                }
            }

            return sorted;
        }

        /// Tính lại giá trị các IDs trung gian theo thứ tự bottom-up đã được sort.
        /// VD: rebuildIds = [b, a] -> tính b = b1_flip + b2, rồi a = b_updated + c
        private void RebuildBottomUp(List<int> rebuildIds, List<SheetKeyValue> values, Parser parser)
        {
            foreach (var nodeId in rebuildIds)
            {
                if (!_parentToFormulas.TryGetValue(nodeId, out var f) || string.IsNullOrEmpty(f?.Formula))
                    continue;
                try
                {
                    var nodeExpr = parser.Parse(f.Formula);
                    var nodeItem = values.FirstOrDefault(x => x.ReportNormID == nodeId);
                    if (nodeItem == null) continue;

                    var deps = nodeExpr.GetDependencies().Distinct().ToList();
                    foreach (var key in nodeItem.Values.Keys.ToList())
                    {
                        var ctx = new CalculationContext(new Dictionary<int, decimal?>());
                        foreach (var dep in deps)
                        {
                            var depItem = values.FirstOrDefault(x => x.ReportNormID == dep);
                            if (depItem != null && depItem.Values.TryGetValue(key, out var v) && v.HasValue)
                                ctx.SetValue(dep, Convert.ToDecimal(v.Value));
                        }
                        try { nodeItem.Values[key] = Convert.ToDecimal(nodeExpr.Evaluate(ctx)); }
                        catch { /* giữ nguyên nếu lỗi */ }
                    }
                    //Log.Information("[KQ][RebuildBottomUp] Tính lại ID={NodeID} Formula={Formula}", nodeId, f.Formula);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[KQ][RebuildBottomUp] Lỗi tính lại ID={NodeID}", nodeId);
                }
            }
        }

        private List<List<int>> GenerateCombinations(List<int> items)
        {
            var result = new List<List<int>>();
            for (int size = 1; size <= items.Count; size++)
                Combine(items, size, 0, new List<int>(), result);
            return result;
        }
        private void Combine(List<int> items, int size, int index, List<int> current, List<List<int>> result)
        {
            if (current.Count == size)
            {
                result.Add(new List<int>(current));
                return;
            }
            for (int i = index; i < items.Count; i++)
            {
                current.Add(items[i]);
                Combine(items, size, i + 1, current, result);
                current.RemoveAt(current.Count - 1);
            }
        }
        private HashSet<FormulaDefinition> GetAllRelatedFormulas(FormulaDefinition root, List<FormulaDefinition> allFormulas, HashSet<string> correctSet)
        {
            var parser = new Parser();

            var formulaDeps = new Dictionary<int, List<int>>();
            var variableToFormulas = new Dictionary<int, HashSet<FormulaDefinition>>();

            foreach (var f in allFormulas.Where(x => x.SheetCode == root.SheetCode))
            {
                if (!f.ReportNormID.HasValue)
                    continue;
                try
                {
                    var deps = parser.Parse(f.Formula).GetDependencies().Distinct().ToList();
                    formulaDeps[f.ReportNormID.Value] = deps;

                    if (!variableToFormulas.ContainsKey(f.ReportNormID.Value))
                        variableToFormulas[f.ReportNormID.Value] = new HashSet<FormulaDefinition>();
                    variableToFormulas[f.ReportNormID.Value].Add(f);

                    foreach (var d in deps)
                    {
                        if (!variableToFormulas.ContainsKey(d))
                            variableToFormulas[d] = new HashSet<FormulaDefinition>();

                        variableToFormulas[d].Add(f);

                        foreach (var prev in variableToFormulas[d])
                            variableToFormulas[d].Add(prev);
                    }
                }
                catch
                {
                    formulaDeps[f.ReportNormID.Value] = new List<int>();
                }
            }

            var result = new HashSet<FormulaDefinition>();
            var visited = new HashSet<int>();

            void DFS(FormulaDefinition f, bool isRoot)
            {
                if (!f.ReportNormID.HasValue)
                    return;

                var depIds = parser.Parse(f.Formula).GetDependencies().Distinct().OrderBy(id => id).ToList();
                string key = $"{f.ReportNormID}|{string.Join(",", depIds)}";

                bool isCorrect = correctSet.Contains(key);
                if (isCorrect)
                    return;

                if (!visited.Add(f.ReportNormID.Value))
                    return;

                result.Add(f);

                //Đi theo CHILD (formula dùng biến nào)
                foreach (var child in depIds)
                {
                    if (variableToFormulas.TryGetValue(child, out var related))
                    {
                        foreach (var rf in related)
                            DFS(rf, false);
                    }
                }
                //ROOT thì KHÔNG đi hướng này
                if (!isRoot)
                {
                    if (variableToFormulas.TryGetValue(f.ReportNormID.Value, out var related))
                    {
                        foreach (var rf in related)
                            DFS(rf, false);
                    }
                }
            }

            DFS(root, true);
            return result;
        }
        private void BlockAllRelatedIds(FormulaError error)
        {
            var parser = new Parser();
            var newlyBlocked = new HashSet<int>();

            void AddBlock(int id)
            {
                if (_blockedAutoAddIds.Add(id))
                    newlyBlocked.Add(id);
            }
            AddBlock(error.Parent.ID);
            foreach (var d in parser.Parse(error.Formula).GetDependencies())
                AddBlock(d);
        }
        private void LogSaiSo(List<FormulaDefinition> allFormulas, FormulaDefinition formula, Expression expr, List<SheetKeyValue> values, List<int> children)
        {
            Log.Information("==== FORMULA ERROR ====");
            Log.Information("Sheet: {Sheet}", formula.SheetCode);
            Log.Information("Parent ID: {Parent}", formula.ReportNormID);
            Log.Information("Formula: {Formula}", formula.Formula);

            var parent = formula.ReportNormID.HasValue ? values.FirstOrDefault(x => x.ReportNormID == formula.ReportNormID.Value) : null;

            var error = new FormulaError
            {
                SheetCode = formula.SheetCode,
                Formula = formula.Formula,
                Parent = new Item
                {
                    ID = formula.ReportNormID.Value,
                    ScanIndex = parent?.ScanIndex ?? 0,
                    Values = parent?.Values != null ? new Dictionary<string, decimal?>(parent.Values) : new Dictionary<string, decimal?>(),
                    IsUpdatePast = parent.IsUpdatePast
                },
                Childs = new List<Item>(),
                Differences = new Dictionary<string, decimal?>(),
            };

            if (parent == null || parent.Values == null || parent.Values.Count == 0)
            {
                Log.Information("ValueParent: NULL");
            }
            else
            {
                var parentLine = string.Join(", ", parent.Values.Select(kv => $"{kv.Key}={kv.Value}"));
                Log.Information("ValueParent: {Values}", parentLine);
            }

            foreach (var c in children)
            {
                var childItem = values.FirstOrDefault(x => x.ReportNormID == c);
                if (childItem == null || childItem.Values == null || childItem.Values.Count == 0)
                {
                    Log.Information("ValueChild @{Child}: NULL", c);
                    continue;
                }

                error.Childs.Add(new Item
                {
                    ID = c,
                    ScanIndex = childItem.ScanIndex,
                    Values = new Dictionary<string, decimal?>(childItem.Values)
                });
                var childLine = string.Join(", ", childItem.Values.Select(kv => $"{kv.Key}={kv.Value}"));
                Log.Information("ValueChild @{Child}: {Values}", c, childLine);
            }

            if (parent?.Values != null && parent.Values.Count > 0)
            {
                var calcByKey = new Dictionary<string, decimal?>();

                foreach (var key in parent.Values.Keys)
                {
                    var ctx = new CalculationContext(new Dictionary<int, decimal?>());

                    foreach (var c in children)
                    {
                        var child = values.FirstOrDefault(x => x.ReportNormID == c);
                        if (child != null && child.Values.TryGetValue(key, out decimal? v) && v.HasValue)
                        {
                            ctx.SetValue(c, Convert.ToDecimal(v.Value));
                        }
                    }

                    try
                    {
                        var result = expr.Evaluate(ctx);
                        calcByKey[key] = result != null ? Convert.ToDecimal(result) : null;
                    }
                    catch
                    {
                        calcByKey[key] = null;
                    }
                }

                var calcStr = string.Join(", ", calcByKey.Select(kv => $"{kv.Key}={kv.Value}"));
                Log.Information("CALC BY FORMULA: {Calc}", calcStr);

                var diffDict = new Dictionary<string, decimal?>();
                foreach (var p in parent.Values)
                {
                    calcByKey.TryGetValue(p.Key, out var c);
                    if (!p.Value.HasValue || !c.HasValue)
                    {
                        diffDict[p.Key] = null;
                    }
                    else
                    {
                        diffDict[p.Key] = p.Value.Value - c.Value;
                    }
                }

                error.Differences = diffDict;
                var diffStr = string.Join(", ", diffDict.Select(kv => kv.Value.HasValue ? $"{kv.Key}={kv.Value.Value}" : $"{kv.Key}=NULL"));
                Log.Information("DIFF (PARENT - FORMULA): {Diff}", diffStr);
            }
            _errors.RemoveAll(x => x.Formula == error.Formula && x.SheetCode == error.SheetCode);
            _errors.Add(error);
            BlockAllRelatedIds(error);

            Log.Information("=========================");
        }
    }
    public static class MappingRowHelper
    {
        public static void AddMissingRowsByReportNorm(FinancialReportModel model, List<SheetKeyValue> mappingValues)
        {
            try
            {
                foreach (var kv in mappingValues)
                {
                    try
                    {
                        var sheetCode = kv.SheetCode ?? string.Empty;
                        var scanIndex = kv.ScanIndex;
                        var valuesFromMapping = kv.Values;

                        //if (valuesFromMapping == null || !valuesFromMapping.Values.Any(v => v.HasValue && v.Value != 0))
                        //    continue;

                        // Lấy sheet tương ứng
                        List<FinancialReportItem>? targetSheet = null;
                        if (!string.IsNullOrEmpty(sheetCode))
                        {
                            if (sheetCode.Contains("CD"))
                                targetSheet = model.BalanceSheet;
                            else if (sheetCode.Contains("KQ"))
                                targetSheet = model.IncomeStatement;
                            else if (sheetCode.Contains("LCGT") || sheetCode.Contains("LCTT"))
                                targetSheet = model.CashFlow;
                            else if (sheetCode.Contains("NB"))
                                targetSheet = model.OffBalanceSheet;
                        }

                        if (targetSheet == null)
                            continue;
                        if (scanIndex != 0)
                        {
                            //Nếu scanIndex != 0 -> Update
                            var existingRow = targetSheet.FirstOrDefault(r => r.ScanIndex == scanIndex);
                            if (existingRow != null)
                            {
                                existingRow.ReportNormID = kv.ReportNormID;

                                foreach (var key in valuesFromMapping.Keys)
                                {
                                    var newVal = valuesFromMapping[key];
                                    if (!existingRow.Values.ContainsKey(key) || existingRow.Values[key] != newVal)
                                    {
                                        //Log.Information("[UPDATE_ROW] ScanIndex={Scan} NormID={ID} Key={Key} ValueOld={Val1} -> ValueNew={Val2}", scanIndex, kv.ReportNormID, key, existingRow.Values.ContainsKey(key) ? existingRow.Values[key] : null, newVal);
                                        existingRow.Values[key] = newVal;
                                    }
                                }
                                continue;
                            }
                        }
                        else
                        {
                            // Nếu scanIndex = 0 -> add
                            if (kv.ReportNormID == null)
                                continue;

                            var existingRow = targetSheet.FirstOrDefault(r => r.ReportNormID == kv.ReportNormID);
                            if (existingRow != null)
                            {
                                foreach (var key in valuesFromMapping.Keys)
                                {
                                    var newVal = valuesFromMapping[key];
                                    if (!existingRow.Values.ContainsKey(key) || existingRow.Values[key] != newVal)
                                    {
                                        existingRow.Values[key] = newVal;
                                    }
                                }
                            }
                            else
                            {
                                var newRow = new FinancialReportItem
                                {
                                    ScanIndex = scanIndex,
                                    ReportNormID = kv.ReportNormID,
                                    Values = valuesFromMapping.ToDictionary(x => x.Key, x => x.Value)
                                };
                                targetSheet.Add(newRow);
                                //Log.Information("[ADD_ROW] Sheet={Sheet} ScanIndex={Scan} NormID={ID}", sheetCode, scanIndex, kv.ReportNormID);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[ADD_ROW][ERROR] ScanIndex={Scan}", kv.ScanIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[ADD_ROW][FATAL] Unable to add missing rows");
            }
        }
    }
    public class HistoricalMappings
    {
        //public static void FlipSignIncomeStatementByPast(FinancialReportModel model, List<ReportDataDetailItem> pastDetails)
        //{
        //    try
        //    {
        //        if (model?.IncomeStatement == null || pastDetails == null || pastDetails.Count == 0)
        //            return;

        //        // ReportNormID -> past item (ưu tiên quý)
        //        var pastLookup = pastDetails
        //            .Where(x => x.Value.HasValue && x.Value.Value != 0)
        //            .GroupBy(x => x.ReportNormID)
        //            .ToDictionary(
        //                g => g.Key,
        //                g => g.FirstOrDefault(x => x.IsCumulative == 0) ?? g.FirstOrDefault(x => x.IsCumulative == 1)
        //            );

        //        bool hasCol1Data = model.IncomeStatement.Any(x => (x?.Values?.Values.ElementAtOrDefault(1) ?? 0) != 0);

        //        foreach (var item in model.IncomeStatement)
        //        {
        //            try
        //            {
        //                if (item?.ReportNormID == null || item.Values == null)
        //                    continue;

        //                if (!pastLookup.TryGetValue(item.ReportNormID.Value, out var past))
        //                    continue;

        //                decimal pastVal = past.Value!.Value;
        //                decimal? currentVal = past.IsCumulative == 0 ? (hasCol1Data ? item.Values.Values.ElementAtOrDefault(1) : item.Values.Values.ElementAtOrDefault(3))
        //                                                             : item.Values.Values.ElementAtOrDefault(3);

        //                if (!currentVal.HasValue || currentVal == 0)
        //                    continue;

        //                // khác dấu -> flip
        //                if ((currentVal < 0) == (pastVal < 0))
        //                    continue;

        //                foreach (var k in item.Values.Keys.ToList())
        //                    if (item.Values[k].HasValue)
        //                        item.Values[k] = -item.Values[k]!.Value;

        //                Log.Information("[FlipSign][KQ] ID={ID} Current={Cur} → {New} (Past={Past})", item.ReportNormID, currentVal, -currentVal, pastVal);
        //            }
        //            catch (Exception exItem)
        //            {
        //                Log.Error(exItem, "[FlipSign][KQ][ItemError] ID={ID}", item?.ReportNormID);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "[FlipSign][KQ][ERROR]");
        //    }
        //}
        //public static void FlipSignIncomeStatementByPast(FinancialReportModel model, List<ReportDataDetailItem> pastDetails)
        //{
        //    try
        //    {
        //        if (model?.IncomeStatement == null || pastDetails == null || pastDetails.Count == 0)
        //            return;

        //        // Lookup: ReportNormID → danh sách giá trị quá khứ (bỏ lũy kế KQ)
        //        var pastLookup = pastDetails
        //            .Where(x => x.Value.HasValue && x.Value.Value != 0
        //                     && !(x.Code != null && x.Code.Contains("KQ") && x.IsCumulative == 1))
        //            .GroupBy(x => x.ReportNormID)
        //            .ToDictionary(g => g.Key, g => g.Select(x => x.Value!.Value).ToList());

        //        foreach (var item in model.IncomeStatement)
        //        {
        //            try
        //            {
        //                if (item?.ReportNormID == null || item.Values == null) continue;
        //                int id = item.ReportNormID.Value;

        //                if (!pastLookup.TryGetValue(id, out var pastValues) || pastValues.Count == 0)
        //                    continue;

        //                // Lấy value hiện tại đại diện (index 0 hoặc 2 tùy có data hay không)
        //                bool hasCol0 = item.Values.Values.Any(v => v.HasValue && v.Value != 0);
        //                var currentVal = hasCol0 ? item.Values.Values.ElementAtOrDefault(0) : item.Values.Values.ElementAtOrDefault(2);

        //                if (!currentVal.HasValue || currentVal.Value == 0) continue;

        //                // So sánh dấu với quá khứ: lấy past value đầu tiên khác 0
        //                decimal pastVal = pastValues.FirstOrDefault(v => v != 0);
        //                if (pastVal == 0) continue;

        //                bool currentNegative = currentVal.Value < 0;
        //                bool pastNegative = pastVal < 0;

        //                // Chỉ đổi dấu khi KHÁC dấu nhau
        //                if (currentNegative == pastNegative) continue;

        //                // Đổi dấu toàn bộ Values của item
        //                foreach (var key in item.Values.Keys.ToList())
        //                {
        //                    if (item.Values[key].HasValue)
        //                        item.Values[key] = -item.Values[key]!.Value;
        //                }

        //                Log.Information("[FlipSign][KQ] ID={ID} | Current={Cur} → flipped to {Flipped} (Past={Past})", id, currentVal.Value, -currentVal.Value, pastVal);
        //            }
        //            catch (Exception exItem)
        //            {
        //                Log.Error(exItem, "[FlipSign][KQ][ItemError] ReportNormID={ID}", item?.ReportNormID);
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "[FlipSign][KQ][ERROR]");
        //    }
        //}
        public static HashSet<int> GetAllIDInFormula(List<FormulaDefinition> allFormula)
        {
            var allIds = new HashSet<int>();
            if (allFormula == null) return allIds;

            var regex = new Regex(@"@(\d+)", RegexOptions.Compiled);

            foreach (var formula in allFormula)
            {
                if (formula == null || !formula.ReportNormID.HasValue) continue;

                allIds.Add(formula.ReportNormID.Value);

                if (string.IsNullOrWhiteSpace(formula.Formula)) continue;

                var matches = regex.Matches(formula.Formula);
                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out int depId))
                    {
                        allIds.Add(depId);
                    }
                }
            }

            return allIds;
        }
        public static List<ReportDataDetailItem> GetIDNoneFormula(HashSet<int> allIDInFormula, List<ReportDataDetailItem> dataQK)
        {
            var result = new List<ReportDataDetailItem>();

            var filtered = dataQK.Where(item => !allIDInFormula.Contains(item.ReportNormID) && item.Value.HasValue).ToList();

            var valueCounts = filtered.GroupBy(item => item.Value.Value).ToDictionary(g => g.Key, g => g.Count());

            result = filtered.Where(item => valueCounts[item.Value.Value] == 1).ToList();

            return result;
        }
        public void MapScanIndexToReportData(FinancialReportModel model, List<SheetKeyValue> values, List<ReportDataDetailItem> allDetails)
        {
            try
            {
                if (model == null || allDetails == null)
                    return;

                var filteredDetails = allDetails
                    .Where(x => !(
                        (x.Code != null && x.Code.Contains("KQ") && x.IsCumulative == 1) ||
                        (x.Code != null && x.Code.Contains("NB") && x.IsCumulative == 1)
                    ))
                    .ToList();

                var detailLookup = filteredDetails
                    .Where(d => d.Code != null && d.Value.HasValue && d.Value.Value != 0)
                    .ToLookup(d => (d.Code, Value: Math.Abs(d.Value.Value)));

                bool useCol2ForCashFlow = model.CashFlow != null && model.CashFlow.Any(x =>
                    x?.Values?.Values != null &&
                    (
                        (x.Values.Values.ElementAtOrDefault(0) ?? 0) != 0 ||
                        (x.Values.Values.ElementAtOrDefault(1) ?? 0) != 0
                    ));

                FixNullReportNormID(model, values, detailLookup, useCol2ForCashFlow);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MapScanIndexToReportData][ERROR]");
            }
        }
        public void FixNullReportNormID(FinancialReportModel model, List<SheetKeyValue> values, ILookup<(string Code, decimal Value), ReportDataDetailItem> detailLookup, bool useCol2ForCashFlow)
        {
            try
            {
                bool isCashFlowIndirect = string.Equals(model.CashFlowMethod, "indirect", StringComparison.OrdinalIgnoreCase);

                var sheets = new Dictionary<string, List<FinancialReportItem>>
                {
                    ["CD"] = model.BalanceSheet,
                    ["KQ"] = model.IncomeStatement,
                    ["LCGT"] = isCashFlowIndirect ? model.CashFlow : null,
                    ["LCTT"] = !isCashFlowIndirect ? model.CashFlow : null,
                    ["NB"] = model.OffBalanceSheet
                };

                foreach (var (code, sheet) in sheets)
                {
                    if (sheet == null || sheet.Count == 0)
                        continue;

                    int valueIndex = (code == "LCGT" || code == "LCTT") ? (useCol2ForCashFlow ? 1 : 3) : 1;

                    foreach (var item in sheet)
                    {
                        try
                        {
                            if (item?.ReportNormID != null || item?.Values == null)
                                continue;

                            var value = item.Values.Values.ElementAtOrDefault(valueIndex);

                            if (!value.HasValue || value.Value == 0)
                                continue;

                            if (!detailLookup.Contains((code, Math.Abs(value.Value))))
                                continue;

                            var candidateIds = detailLookup[(code, Math.Abs(value.Value))].Select(d => d.ReportNormID).Distinct().ToList();

                            if (candidateIds.Count == 0)
                                continue;

                            // Tính existingIds realtime để tránh stale data
                            var allItems = sheets.Values.Where(s => s != null).SelectMany(s => s);

                            var existingIds = allItems.Where(x => x.ReportNormID != null).Select(x => x.ReportNormID.Value).ToHashSet();

                            int idToAssign;

                            if (candidateIds.Count == 1)
                            {
                                idToAssign = candidateIds[0];
                            }
                            else
                            {
                                var available = candidateIds.Where(id => !existingIds.Contains(id)).ToList();
                                if (available.Count > 1) continue;
                                idToAssign = available.Count > 0 ? available.FirstOrDefault() : candidateIds.FirstOrDefault();
                            }

                            if (idToAssign == 0)
                                continue;

                            if (item.ReportNormID == idToAssign)
                                continue;

                            //Swap nếu ID đang nằm ở item khác
                            var existingItem = allItems.FirstOrDefault(x => x.ReportNormID == idToAssign);

                            if (existingItem != null && existingItem != item)
                            {
                                existingItem.ReportNormID = null;

                                var oldValueItem = values.FirstOrDefault(v => v.SheetCode == code && v.ScanIndex == existingItem.ScanIndex);

                                if (oldValueItem != null)
                                    oldValueItem.ReportNormID = null;
                            }

                            //Gán ID cho item hiện tại
                            item.ReportNormID = idToAssign;

                            var newValueItem = values.FirstOrDefault(v => v.SheetCode == code && v.ScanIndex == item.ScanIndex);

                            if (newValueItem != null)
                                newValueItem.ReportNormID = idToAssign;

                            Log.Information("[AutoMap] {code}: Value={value} → ReportNormID={id}", code, value.Value, idToAssign);
                        }
                        catch (Exception exItem)
                        {
                            Log.Error(exItem, "[FixNullReportNormID][ItemError] Code={code} ScanIndex={scan}", code, item?.ScanIndex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FixNullReportNormID][ERROR]");
            }
        }
        public List<IDError> CheckDataPast(FinancialReportModel model, List<ReportDataDetailItem> allDetails)
        {
            var errors = new List<IDError>();
            try
            {
                if (model == null || allDetails == null)
                    return errors;

                // Bỏ NB lũy kế
                var filteredDetails = allDetails.Where(x => !(x.Code != null && x.Code.Contains("NB") && x.IsCumulative == 1)).ToList();

                // Lookup theo ReportNormID + IsLuyKe
                var detailLookup = filteredDetails
                    .Where(x => x.Value.HasValue)
                    .GroupBy(x => (x.ReportNormID, x.IsCumulative))
                    .ToDictionary(g => g.Key, g => g.First());

                bool isCashFlowIndirect = string.Equals(model.CashFlowMethod, "indirect", StringComparison.OrdinalIgnoreCase);
                var sheets = new Dictionary<string, List<FinancialReportItem>>
                {
                    ["CD"] = model.BalanceSheet,
                    ["KQ"] = model.IncomeStatement,
                    ["NB"] = model.OffBalanceSheet,
                    ["LCGT"] = isCashFlowIndirect ? model.CashFlow : null,
                    ["LCTT"] = !isCashFlowIndirect ? model.CashFlow : null
                };

                foreach (var (code, sheet) in sheets)
                {
                    if (sheet == null || sheet.Count == 0)
                        continue;

                    // Kiểm tra bảng có 4 cột dữ liệu hay chỉ 2
                    bool useColFL1 = sheet.Any(x => x?.Values?.Values != null &&
                                        ((x.Values.Values.ElementAtOrDefault(0) ?? 0) != 0 ||
                                         (x.Values.Values.ElementAtOrDefault(1) ?? 0) != 0));
                    bool useColFL3 = sheet.Any(x => x?.Values?.Values != null &&
                                        ((x.Values.Values.ElementAtOrDefault(2) ?? 0) != 0 ||
                                         (x.Values.Values.ElementAtOrDefault(3) ?? 0) != 0));
                    bool isForceTwoColumn = model?.Meta?.KyBaoCao == "Q1" || model?.Meta?.KyBaoCao == "N";

                    bool isFourColumns = !isForceTwoColumn && useColFL1 && useColFL3;

                    foreach (var item in sheet)
                    {
                        try
                        {
                            if (item?.ReportNormID == null || item.Values?.Values == null)
                                continue;


                            bool isError = false;
                            int id = item.ReportNormID.Value;
                            var values = item.Values.Values;

                            if (!isFourColumns)
                            {
                                // Chỉ có 2 cột -> so sánh theo ID, bỏ qua IsLuyKe
                                var modelValue = values.ElementAtOrDefault(1) ?? values.ElementAtOrDefault(3);

                                if (modelValue.HasValue)
                                {
                                    var detail = filteredDetails.FirstOrDefault(d => d.ReportNormID == id && d.Value.HasValue);

                                    var detailValue = detail?.Value;
                                    //isError = detailValue == null || Math.Abs(modelValue.Value - detailValue.Value) > 3m;
                                    bool isBothEmpty = (!modelValue.HasValue || modelValue.Value == 0) && (!detailValue.HasValue || detailValue.Value == 0);

                                    if (!isBothEmpty)
                                    {
                                        isError = !modelValue.HasValue || !detailValue.HasValue || !(modelValue.Value == detailValue.Value || modelValue.Value == -detailValue.Value  || Math.Abs(modelValue.Value - detailValue.Value) <= 3m);
                                    }

                                    if (isError)
                                    {
                                        errors.Add(new IDError
                                        {
                                            SheetCode = code,
                                            ReportNormID = id
                                        });

                                        Log.Warning("[CheckPast][{code}] ID={id} LỆCH | PDF={pVal} DataPast={dVal}", code, id, modelValue, detailValue);
                                    }
                                }
                                continue;
                            }

                            // 4 cột dữ liệu
                            var valIdx1 = values.ElementAtOrDefault(1); // IsLuyKe = 0
                            var valIdx3 = values.ElementAtOrDefault(3); // IsLuyKe = 1

                            var hasDetail0 = detailLookup.TryGetValue((id, 0), out var d0);
                            var hasDetail1 = detailLookup.TryGetValue((id, 1), out var d1);

                            var detailVal0 = d0?.Value;
                            var detailVal1 = d1?.Value;

                            bool hasSheet0 = valIdx1.HasValue && valIdx1.Value != 0;
                            bool hasSheet1 = valIdx3.HasValue && valIdx3.Value != 0;

                            bool hasDetailVal0 = hasDetail0 && detailVal0.HasValue && detailVal0.Value != 0;
                            bool hasDetailVal1 = hasDetail1 && detailVal1.HasValue && detailVal1.Value != 0;

                            // ===== CHECK TỪNG CỘT =====
                            bool error0 = false;
                            bool error1 = false;

                            // Cột quý
                            if (hasSheet0 || hasDetailVal0)
                            {
                                error0 = !hasSheet0 || !hasDetailVal0 || !(valIdx1.Value == detailVal0.Value || valIdx1.Value == -detailVal0.Value || Math.Abs(valIdx1.Value - detailVal0.Value) <= 3m);
                            }

                            // Cột lũy kế
                            if (hasSheet1 || hasDetailVal1)
                            {
                                error1 = !hasSheet1 || !hasDetailVal1 || !(valIdx3.Value == detailVal1.Value || valIdx3.Value == -detailVal1.Value || Math.Abs(valIdx3.Value - detailVal1.Value) <= 3m);
                            }

                            isError = error0 || error1;

                            if (isError)
                            {
                                errors.Add(new IDError
                                {
                                    SheetCode = code,
                                    ReportNormID = id
                                });

                                Log.Warning("[CheckPast][{code}] ID={id} LỆCH | Quý: PDF={p0} DataPast={d0} | Lũy kế: PDF={p1} DataPast={d1}", code, id, valIdx1, detailVal0, valIdx3, detailVal1);
                            }
                        }
                        catch (Exception exItem)
                        {
                            Log.Error(exItem, "[CheckPast][ItemError] Sheet={code} ReportNormID={id}", code, item?.ReportNormID);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CheckDataPast][ERROR]");
            }
            return errors;
        }
        public void GetFormulaID(FinancialReportModel model, List<IDError> ids, List<FormulaDefinition> allFormula, ResultCheckFormula formulaError)
        {
            if (model == null || ids == null || allFormula == null)
                return;

            foreach (var idError in ids)
            {
                int idToCheck = idError.ReportNormID;
                string sheetCode = idError.SheetCode;

                // Chỉ check nếu ID còn trong IDCorrects
                if (!formulaError.IDCorrects.Contains(idToCheck))
                    continue;

                // Lấy tất cả công thức có child liên quan ID này
                var relatedFormulas = allFormula
                    .Where(f => !string.IsNullOrEmpty(f.SheetCode) && f.SheetCode.Contains(sheetCode) && f.ReportNormID != idToCheck && (f.Formula ?? "").Contains($"@{idToCheck}"))
                    .ToList();

                foreach (var formulaDef in relatedFormulas)
                {
                    if (!formulaDef.ReportNormID.HasValue) continue;

                    int parentId = formulaDef.ReportNormID.Value;

                    // Tách tất cả child IDs từ công thức
                    var childIds = Regex.Matches(formulaDef.Formula ?? "", @"@(\d+)")
                                        .Select(m => int.Parse(m.Groups[1].Value))
                                        .Distinct()
                                        .ToList();

                    //Chỉ lấy những công thức nhiều child vì có thể lộn 
                    if (childIds.Count <= 5)
                        continue;

                    // Bao gồm ParentID
                    var allIdsInFormula = new HashSet<int>(childIds) { parentId };

                    // Lấy sheet tương ứng
                    var sheet = sheetCode switch
                    {
                        "CD" => model.BalanceSheet,
                        "KQ" => model.IncomeStatement,
                        "NB" => model.OffBalanceSheet,
                        "LCGT" => model.CashFlow,
                        "LCTT" => model.CashFlow,
                        _ => null
                    };
                    if (sheet == null)
                        continue;

                    // Lấy ParentValues
                    var parentItem = sheet.FirstOrDefault(x => x.ReportNormID == parentId);
                    if (parentItem == null || parentItem.Values?.Values == null)
                        continue;

                    formulaError.FormulaErrors.Add(new FormulaError
                    {
                        SheetCode = sheetCode,
                        Formula = formulaDef.Formula ?? "",
                        Parent = new Item 
                        {
                            ID = parentId,
                            ScanIndex = parentItem.ScanIndex,
                            Values = new Dictionary<string, decimal?>(parentItem.Values)
                        },
                        Childs = childIds
                            .Select(childId =>
                            {
                                var childItem = sheet.FirstOrDefault(x => x.ReportNormID == childId);
                                if (childItem?.Values?.Values == null) return null;
                                return new Item
                                {
                                    ID = childId,
                                    ScanIndex = childItem.ScanIndex,
                                    Values = new Dictionary<string, decimal?>(childItem.Values)
                                };
                            })
                            .Where(x => x != null)
                            .ToList(),
                        Differences = null
                    });

                    // Loại bỏ tất cả ID trong công thức khỏi IDCorrects
                    formulaError.IDCorrects.RemoveAll(id => allIdsInFormula.Contains(id));

                }
            }
        }
    }

}
