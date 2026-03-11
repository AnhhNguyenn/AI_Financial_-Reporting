/*using BCTC.DataAccess.Models;
using Serilog;
using System.Text.Json;

namespace BCTC.BusinessLogic.OcrLogic
{
    public static class ParentValuePropagator
    {
        private static Dictionary<string, Dictionary<string, string[]>> _currentFormulas = new();

        private static void LoadFormulasByBizType(int bizType, string wwwRoot)
        {
            string fileName = bizType switch
            {
                2 => "FormulaCK.json",
                3 => "FormulaNH.json",
                5 => "FormulaBH.json",
                _ => "FormulaCP.json"
            };

            string path = Path.Combine(wwwRoot, "Formula", fileName);

            try
            {
                if (File.Exists(path))
                {
                    string jsonContent = File.ReadAllText(path);
                    _currentFormulas = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string[]>>>(jsonContent) ?? new();
                    Log.Information("[FORMULA] Loaded {File} for BizType {Id}", fileName, bizType);
                }
                else
                {
                    Log.Warning("[FORMULA] File not found: {Path}", path);
                    _currentFormulas = new();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FORMULA] Error loading JSON from {Path}", path);
                _currentFormulas = new();
            }
        }

        public static void PropagateParentToChild(ExtractResult result, string wwwRoot)
        {
            if (result == null) return;
            int bizType = result.BusinessTypeID ?? 1;

            LoadFormulasByBizType(bizType, wwwRoot);

            if (bizType == 2 && result.IncomeStatement != null)
            {
                MergeConsultingRevenue(result.IncomeStatement);
            }

            ApplyUnifiedLogic(result.BalanceSheet, "CD");
            ApplyUnifiedLogic(result.IncomeStatement, "KQ");
            ApplyUnifiedLogic(result.OffBalanceSheet, "NB");
            ApplyUnifiedLogic(result.CashFlow, "LCGT");
            ApplyUnifiedLogic(result.CashFlow, "LCTT");

            EnforcePositiveSigns(result);
        }

        private static void ApplyUnifiedLogic(List<Row> rows, string sectionKey)
        {
            if (rows == null || !_currentFormulas.TryGetValue(sectionKey, out var rules)) return;

            var rowMap = rows.Where(r => r.ReportNormID != null).ToDictionary(r => r.ReportNormID!);
            var years = rows.SelectMany(r => r.Values?.Keys ?? Enumerable.Empty<string>()).Distinct().ToList();

            foreach (var year in years)
            {
                foreach (var rule in rules)
                {
                    string parentId = rule.Key;
                    string[] childrenIds = rule.Value;

                    double sumChildren = 0;
                    bool hasChildData = false;

                    foreach (var id in childrenIds)
                    {
                        bool isNegative = id.StartsWith("-");
                        string normId = id.TrimStart('-');

                        if (rowMap.TryGetValue(normId, out var r) && r.Values != null &&
                            r.Values.TryGetValue(year, out var v) && v.HasValue && v.Value != 0)
                        {
                            sumChildren += isNegative ? -v.Value : v.Value;
                            hasChildData = true;
                        }
                    }

                    if (hasChildData)
                    {
                        SetRowValue(rows, rowMap, parentId, year, sumChildren, "Auto-sum from children");
                    }
                    else
                    {
                        if (rowMap.TryGetValue(parentId, out var pRow) && pRow.Values != null &&
                            pRow.Values.TryGetValue(year, out var pVal) && pVal.HasValue && pVal.Value != 0)
                        {
                            string firstChildId = childrenIds[0].TrimStart('-');
                            SetRowValue(rows, rowMap, firstChildId, year, pVal.Value, $"(auto) Propagated from parent {parentId}");
                        }
                    }
                }
            }
        }

        private static void SetRowValue(List<Row> section, Dictionary<string, Row> rowMap, string id, string year, double val, string note = "")
        {
            if (!rowMap.TryGetValue(id, out var row))
            {
                row = new Row
                {
                    ReportNormID = id,
                    Item = note,
                    Values = new Dictionary<string, double?>(),
                    Code = ""
                };
                section.Add(row);
                rowMap[id] = row;
            }
            row.Values ??= new Dictionary<string, double?>();
            row.Values[year] = val;
        }

        public static void MergeConsultingRevenue(List<Row> rows)
        {
            if (rows == null || rows.Count == 0) return;
            string[] destKeywords = { "doanh thu hoạt động tư vấn tài chính", "doanh thu nghiệp vụ tư vấn tài chính" };
            string sourceKeyword = "doanh thu nghiệp vụ tư vấn đầu tư chứng khoán";

            var destRow = rows.FirstOrDefault(r => r.Code == "08" || destKeywords.Any(k => r.Item.ToLower().Contains(k)));
            var sourceRow = rows.FirstOrDefault(r => r.Item.ToLower().Contains(sourceKeyword));

            if (destRow == null && sourceRow != null)
            {
                sourceRow.ReportNormID = "4603";
                sourceRow.Code = "08";
                return;
            }

            if (destRow != null && sourceRow != null)
            {
                destRow.ReportNormID = "4603";
                destRow.Code = "08";
                if (sourceRow.Values != null)
                {
                    destRow.Values ??= new Dictionary<string, double?>();
                    foreach (var kv in sourceRow.Values)
                    {
                        if (!kv.Value.HasValue) continue;
                        if (destRow.Values.ContainsKey(kv.Key))
                            destRow.Values[kv.Key] = (destRow.Values[kv.Key] ?? 0) + kv.Value.Value;
                        else
                            destRow.Values[kv.Key] = kv.Value.Value;
                    }
                }
                rows.Remove(sourceRow);
            }
        }

        private static void EnforcePositiveSigns(ExtractResult result)
        {
            var PositiveSignRules = new Dictionary<int, HashSet<string>>
            {
                { 1, new HashSet<string> { "2218", "2207", "2222", "2223", "2227", "2224", "2226" } },
                { 2, new HashSet<string> { "4598" } }
            };

            int bizType = result.BusinessTypeID ?? 1;
            if (!PositiveSignRules.TryGetValue(bizType, out var normsToFix)) return;

            var section = result.IncomeStatement;
            if (section == null) return;

            foreach (var row in section.Where(r => r.ReportNormID != null && normsToFix.Contains(r.ReportNormID)))
            {
                if (row.Values == null) continue;
                foreach (var key in row.Values.Keys.ToList())
                {
                    if (row.Values[key] < 0) row.Values[key] = Math.Abs(row.Values[key].Value);
                }
            }
        }
    }
}*/