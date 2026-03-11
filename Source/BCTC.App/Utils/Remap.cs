using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BCTC.DataAccess.Models;
using MappingReportNorm.Models;
using MappingReportNorm.Services;
using Serilog;

namespace BCTC.App.Utils
{
    public enum CandidateStatus
    {
        NEED_AI_CHOICE,
        NO_SOLUTION_KEEP_ORIGINAL
    }

    public class CandidateItem
    {
        public int ScanIndex { get; set; }
        public int? ReportNormID { get; set; }
        public string SheetCode { get; set; }
        public string Name { get; set; }
        public Dictionary<string, decimal?> Values = new();
        public int Source { get; set; } = 0;    //0: chỉ 1 nguồn thích hợp, 1: hiện tại thích hợp, 2: quá khứ thích hợp
    }
    public class CandidateSource
    {
        public int Index { get; set; } 
        public List<CandidateItem> CandidateItems { get; set; }
        public int Source { get; set; } = 0;    //0: chỉ 1 nguồn thích hợp, 1: hiện tại thích hợp, 2: quá khứ thích hợp
    }
    public class CandidateChoose
    {
        public int ParentID { get; set; }
        public List<CandidateItem> ParentPDF { get; set; }
        public List<int> ChildIDs { get; set; }
        public List<CandidateSource> ChildPDFs { get; set; }
    }
    public class FormulaSlot
    {
        public int ChildID { get; set; }
        public int Sign { get; set; } // +1 or -1
    }
    public class MatchEntry 
    {
        public int ParentId { get; set; }
        public int SourceType { get; set; }    // sourceType: 0 = 1 nguồn duy nhất, 1 = hiện tại, 2 = quá khứ
        public List<CandidateItem> Candidates { get; set; }
    }
    public class FormulaCandidate
    {
        public string SheetCode { get; set; }
        public int ParentID { get; set; }
        public string Formula { get; set; }
        public List<CandidateChoose> MatchedCombinations { get; set; }
        public CandidateStatus Status { get; set; }
        public string Reason { get; set; }
    }

    public class CandidateCollector
    {
        private static readonly string[] SHEET_CODES = { "CD", "KQ", "LCGT", "NB" };
        private static void CollectSheet(List<FinancialReportItem> rows, string sheetCode, List<CandidateItem> result)
        {
            if (rows == null || rows.Count == 0)
                return;

            foreach (var row in rows)
            {
                try
                {
                    if (row?.Values == null)
                        continue;

                    result.Add(new CandidateItem
                    {
                        ScanIndex = row.ScanIndex,
                        SheetCode = sheetCode,
                        ReportNormID = row.ReportNormID,
                        Name = row.Item ?? string.Empty,
                        Values = row.Values
                            .Where(v => v.Value.HasValue)
                            .ToDictionary(
                                v => v.Key,
                                v => (decimal?)v.Value.Value
                            )
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[CollectSheet] Sheet={Sheet} ScanIndex={ScanIndex} ReportNormID={NormID}", sheetCode, row?.ScanIndex, row?.ReportNormID);
                }
            }
        }
        public static List<CandidateItem> Collect(FinancialReportModel model)
        {
            var result = new List<CandidateItem>();

            if (model == null)
                return result;

            CollectSheet(model.IncomeStatement, "KQ", result);
            CollectSheet(model.BalanceSheet, "CD", result);
            string cashFlowCode = string.Equals(model.CashFlowMethod, "Indirect", StringComparison.OrdinalIgnoreCase) ? "LCGT" : "LCTT";
            CollectSheet(model.CashFlow, cashFlowCode, result);
            CollectSheet(model.OffBalanceSheet, "NB", result);

            return result;
        }
    }

    public class FormulaAutoRemap
    {
        private const int SCAN_INDEX_OFFSET = 5;
        public List<FormulaCandidate> Process(ResultCheckFormula errors, FinancialReportModel model)
        {
            var results = new List<FormulaCandidate>();

            try
            {
                var allCandidates = CandidateCollector.Collect(model);

                foreach (var error in errors.FormulaErrors)
                {
                    try
                    {
                        // TRƯỜNG HỢP DIFFERENCES -> NULL
                        if (error.Differences == null)
                        {
                            var parentItem = FindItemInModel(model, error.SheetCode, error.Parent.ID);

                            var childIdsInFormula = ParseSlots(error.Formula).Select(s => s.ChildID).Where(id => !errors.IDDefaults.Contains(id)).Distinct().ToList();

                            var combinedChildren = new List<CandidateItem>();

                            foreach (var childId in childIdsInFormula)
                            {
                                var childItem = FindItemInModel(model, error.SheetCode, childId);
                                if (childItem != null)
                                    combinedChildren.Add(childItem);
                            }

                            var childPDFs = new List<CandidateSource>();
                            if (combinedChildren.Any())
                            {
                                childPDFs.Add(new CandidateSource
                                {
                                    Index = 1,
                                    Source = 0,
                                    CandidateItems = combinedChildren
                                });
                            }

                            var choose = new CandidateChoose
                            {
                                ParentID = error.Parent.ID,
                                ParentPDF = parentItem != null ? new List<CandidateItem> { parentItem } : new List<CandidateItem>(),
                                ChildIDs = childIdsInFormula,
                                ChildPDFs = childPDFs
                            };

                            var fc = new FormulaCandidate
                            {
                                SheetCode = error.SheetCode,
                                ParentID = error.Parent.ID,
                                Formula = error.Formula,
                                MatchedCombinations = new List<CandidateChoose> { choose },
                                Status = CandidateStatus.NEED_AI_CHOICE,
                                Reason = "Differences is null – single combined candidate set"
                            };

                            results.Add(fc);
                            continue;
                        }

                        var fcNormal = ProcessOne(error, allCandidates, errors.IDCorrects, errors.IDDefaults);
                        results.Add(fcNormal);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[AUTO_REMAP][ERROR] ({ex}) Parent={Parent} Sheet={Sheet}", ex, error.Parent.ID, error.SheetCode);

                        results.Add(new FormulaCandidate
                        {
                            ParentID = error.Parent.ID,
                            SheetCode = error.SheetCode,
                            Formula = error.Formula,
                            Status = CandidateStatus.NO_SOLUTION_KEEP_ORIGINAL,
                            Reason = "exception"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[AUTO_REMAP][FATAL] Cannot process auto remap");
            }

            return results;
        }
        private FormulaCandidate ProcessOne(FormulaError error, List<CandidateItem> allCandidates, List<int> correctIds, List<int> IDDefaults)
        {
            try
            {
                var mainSlots = ParseSlots(error.Formula);
                var mainIds = new HashSet<int>(mainSlots.Select(s => s.ChildID));

                var candidates = allCandidates
                                .Where(x => !string.IsNullOrEmpty(x.SheetCode) && !string.IsNullOrEmpty(error.SheetCode) && error.SheetCode.ToUpper().Contains(x.SheetCode.ToUpper()))
                                .ToList();

                // ===== lọc ScanIndex ±5 (GIỮ NGUYÊN LOGIC CŨ) =====
                var relatedIds = new HashSet<int>(mainSlots.Select(s => s.ChildID));
                relatedIds.Add(error.Parent.ID);

                var occupiedScanIndexes = candidates.Where(c => c.ReportNormID != null && relatedIds.Contains(c.ReportNormID.Value)).Select(c => c.ScanIndex).Distinct().ToList();

                if (occupiedScanIndexes.Any())
                {
                    int min = Math.Max(0, occupiedScanIndexes.Min() - SCAN_INDEX_OFFSET);
                    int max = occupiedScanIndexes.Max() + SCAN_INDEX_OFFSET;

                    candidates = candidates.Where(c => c.ScanIndex != 0)
                                            .Where(c => c.ScanIndex >= min && c.ScanIndex <= max)
                                            .Where(c => (c.ReportNormID == null || (!correctIds.Contains(c.ReportNormID.Value) && !IDDefaults.Contains(c.ReportNormID.Value))) || mainIds.Contains(c.ReportNormID.Value))
                                            .Where(c => c.Values != null && c.Values.Any(v => v.Value.HasValue && v.Value.Value != 0))
                                            .ToList();
                }

                //Kiểm tra ứng viên - candidates
                if (candidates.Count == 0)
                {
                    return new FormulaCandidate
                    {
                        ParentID = error.Parent.ID,
                        SheetCode = error.SheetCode,
                        Formula = error.Formula,
                        Status = CandidateStatus.NO_SOLUTION_KEEP_ORIGINAL,
                        Reason = "No candidates found after filtering"
                    };
                }

                var results = new List<MatchEntry>();

                // TH1: parent cố địnhturn

                if (error.Parent.Values?.Count > 0)
                {
                    var parentItem = candidates.FirstOrDefault(c => c.ReportNormID == error.Parent.ID);

                    var childCandidates = (parentItem != null && parentItem.Values?.Count > 0)
                        ? candidates.Where(c => c.ScanIndex != parentItem.ScanIndex).ToList()
                        : candidates.ToList();

                    bool matchedAll = true;

                    var formulaSlots = ParseSlots(error.Formula);
                    var defaultSlots = formulaSlots.Where(s => IDDefaults.Contains(s.ChildID)).ToList();

                    // Tính parentAdjusted
                    var parentAdjusted = new Dictionary<string, decimal?>();

                    foreach (var kv in error.Parent.Values)
                    {
                        var year = kv.Key;
                        decimal parentVal = kv.Value ?? 0;
                        decimal adjust = 0;

                        foreach (var slot in defaultSlots)
                        {
                            var defaultCandidate = allCandidates.FirstOrDefault(c => c.ReportNormID == slot.ChildID);

                            if (defaultCandidate == null)
                                continue;

                            if (!defaultCandidate.Values.TryGetValue(year, out var v) || !v.HasValue)
                                continue;

                            adjust += slot.Sign * v.Value;
                        }

                        parentAdjusted[year] = parentVal - adjust;
                    }

                    // Loại default slot khỏi match
                    var slotsForMatch = formulaSlots.Where(s => !IDDefaults.Contains(s.ChildID)).ToList();

                    //var temp = new List<Dictionary<int, List<CandidateItem>>>();

                    //TryMatch(error.SheetCode, error.Parent.IsUpdatePast, parentAdjusted, childCandidates, slotsForMatch, error.Parent.ID, temp);
                    //th1Results.AddRange(temp);

                    if (!error.Parent.IsUpdatePast)
                    {
                        // isPast = false → chỉ check hiện tại, sourceType = 0
                        var tempResults = new List<MatchEntry>();
                        TryMatch(error.SheetCode, isPast: false, parentAdjusted, childCandidates, slotsForMatch, error.Parent.ID, tempResults, sourceType: 0);
                        results.AddRange(tempResults);
                    }
                    else
                    {
                        // isPast = true → tìm match hiện tại (sourceType=1) VÀ quá khứ (sourceType=2)
                        TryMatch(error.SheetCode, isPast: false, parentAdjusted, childCandidates, slotsForMatch, error.Parent.ID, results, sourceType: 1);
                        TryMatch(error.SheetCode, isPast: true, parentAdjusted, childCandidates, slotsForMatch, error.Parent.ID, results, sourceType: 2);
                    }

                    if (results.Count > 0)
                        return BuildResult(error, results, IDDefaults);
                    //if (th1Results.Count > 0)
                    //    return BuildResult(error, th1Results, IDDefaults);
                }

                return new FormulaCandidate
                {
                    ParentID = error.Parent.ID,
                    SheetCode = error.SheetCode,
                    Formula = error.Formula,
                    Status = CandidateStatus.NO_SOLUTION_KEEP_ORIGINAL,
                    Reason = "No valid formula match"
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AUTO_REMAP][FAIL] ({ex}) Parent={Parent} Formula={Formula}", ex, error.Parent.ID, error.Formula);

                return new FormulaCandidate
                {
                    ParentID = error.Parent.ID,
                    SheetCode = error.SheetCode,
                    Formula = error.Formula,
                    Status = CandidateStatus.NO_SOLUTION_KEEP_ORIGINAL,
                    Reason = "exception"
                };
            }
        }
        //private void TryMatch(string sheetCode, bool isPast, Dictionary<string, decimal?> parentValues, List<CandidateItem> childCandidates, List<FormulaSlot> slots, int parentId, List<Dictionary<int, List<CandidateItem>>> results)
        //{
        //    if (parentValues == null || parentValues.Count == 0)
        //    {
        //        Log.Warning("[TryMatch] ParentValues null/empty. ParentID={ParentID}", parentId);
        //        return;
        //    }
        //    if (childCandidates == null || childCandidates.Count == 0)
        //    {
        //        Log.Warning("[TryMatch] No child candidates. ParentID={ParentID}", parentId);
        //        return;
        //    }
        //    if (slots == null || slots.Count == 0)
        //    {
        //        Log.Warning("[TryMatch] Slots null/empty. ParentID={ParentID}", parentId);
        //        return;
        //    }
        //    results ??= new List<Dictionary<int, List<CandidateItem>>>(); //
        //    try
        //    {
        //        //Xử lí dấu riêng trường hợp KQ
        //        bool isKQ = sheetCode?.Contains("KQ", StringComparison.OrdinalIgnoreCase) == true;

        //        int plusQuota = slots.Count(s => s.Sign == 1);
        //        int minusQuota = slots.Count(s => s.Sign == -1);
        //        //var targetScanIndices = new HashSet<int> { 14, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 };
        //        //Toàn cộng hoặc toàn trừ
        //        bool isAllPlus = plusQuota > 0 && minusQuota == 0;
        //        bool isAllMinus = plusQuota == 0 && minusQuota > 0;

        //        var keysToCheck = parentValues.Keys.Where((key, index) => isPast ? (index == 1 || index == 3) : (index == 0 || index == 2)).ToList();
        //        int kMax = Math.Min(childCandidates.Count, slots.Count);
        //        bool stop = false;
        //        for (int k = 1; k <= kMax; k++)
        //        {
        //            if (stop) break;
        //            foreach (var cs in ChooseCandidates(childCandidates, k))
        //            {
        //                if (stop) break;

        //                if (isAllPlus) // Toàn cộng
        //                {
        //                    bool ok = false;
        //                    int flipMax = isKQ ? (1 << k) : 1;

        //                    for (int flipMask = 0; flipMask < flipMax && !ok; flipMask++)
        //                    {
        //                        bool match = true;

        //                        foreach (var year in keysToCheck)
        //                        {
        //                            decimal sum = 0;

        //                            for (int i = 0; i < k; i++)
        //                            {
        //                                if (!cs[i].Values.TryGetValue(year, out var v) || !v.HasValue)
        //                                    continue;

        //                                var val = v.Value;

        //                                if (isKQ && ((flipMask & (1 << i)) != 0))
        //                                    val = -val;

        //                                sum += val;
        //                            }

        //                            if (Math.Abs(sum - (parentValues[year] ?? 0)) > 1)
        //                            {
        //                                match = false;
        //                                break;
        //                            }
        //                        }

        //                        if (match)
        //                            ok = true;
        //                    }

        //                    if (ok)
        //                    {
        //                        if (k >= 5)
        //                            stop = true;

        //                        results.Add(new Dictionary<int, List<CandidateItem>>
        //                        {
        //                            { parentId, cs.ToList() }
        //                        });
        //                        break;
        //                    }
        //                }
        //                else if (isAllMinus) // Toàn trừ
        //                {
        //                    bool ok = false;
        //                    int flipMax = isKQ ? (1 << k) : 1;  // nếu KQ thì thử 2^k tổ hợp

        //                    for (int flipMask = 0; flipMask < flipMax && !ok; flipMask++)
        //                    {
        //                        bool match = true;

        //                        foreach (var year in keysToCheck)
        //                        {
        //                            decimal sum = 0;

        //                            for (int i = 0; i < k; i++)
        //                            {
        //                                if (!cs[i].Values.TryGetValue(year, out var v) || !v.HasValue)
        //                                    continue;

        //                                var val = v.Value;

        //                                // logic gốc là trừ
        //                                val = -val;

        //                                if (isKQ && ((flipMask & (1 << i)) != 0))
        //                                    val = -val;

        //                                sum += val;
        //                            }

        //                            if (Math.Abs(sum - (parentValues[year] ?? 0)) > 1)
        //                            {
        //                                match = false;
        //                                break;
        //                            }
        //                        }

        //                        if (match)
        //                            ok = true;
        //                    }

        //                    if (ok)
        //                    {
        //                        if (k >= 5)
        //                            stop = true;

        //                        results.Add(new Dictionary<int, List<CandidateItem>>
        //                        {
        //                            { parentId, cs.ToList() }
        //                        });
        //                        break;
        //                    }
        //                }
        //                else // Có cộng có trừ
        //                {
        //                    int maskMax = 1 << k;
        //                    int flipMax = isKQ ? (1 << k) : 1;

        //                    for (int mask = 0; mask < maskMax; mask++)
        //                    {
        //                        int plus = 0, minus = 0;

        //                        for (int i = 0; i < k; i++)
        //                            if ((mask & (1 << i)) != 0) plus++;
        //                            else minus++;

        //                        if (plus > plusQuota || minus > minusQuota)
        //                            continue;

        //                        for (int flipMask = 0; flipMask < flipMax; flipMask++)
        //                        {
        //                            bool ok = true;

        //                            foreach (var year in keysToCheck)
        //                            {
        //                                decimal sum = 0;

        //                                for (int i = 0; i < k; i++)
        //                                {
        //                                    if (!cs[i].Values.TryGetValue(year, out var v) || !v.HasValue)
        //                                        continue;

        //                                    var val = ((mask & (1 << i)) != 0) ? v.Value : -v.Value;

        //                                    // nếu KQ thì cho phép đảo thêm
        //                                    if (isKQ && ((flipMask & (1 << i)) != 0))
        //                                        val = -val;

        //                                    sum += val;
        //                                }

        //                                if (Math.Abs(sum - (parentValues[year] ?? 0)) > 1)
        //                                {
        //                                    ok = false;
        //                                    break;
        //                                }
        //                            }

        //                            if (ok)
        //                            {
        //                                if (k >= 5)
        //                                    stop = true;

        //                                results.Add(new Dictionary<int, List<CandidateItem>>
        //                                {
        //                                    { parentId, cs.ToList() }
        //                                });

        //                                goto END_MASK_LOOP;
        //                            }
        //                        }
        //                    }

        //                END_MASK_LOOP:;
        //                }

        //            }
        //        }
        //    }
        //    catch (ArgumentNullException ex)
        //    {
        //        Log.Error(ex, "[TryMatch][ARG_NULL] ParentID={ParentID} Param={Param}", parentId, ex.ParamName);
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "[TryMatch][UNEXPECTED] ParentID={ParentID}", parentId);
        //    }
        //}
        private void TryMatch(string sheetCode, bool isPast, Dictionary<string, decimal?> parentValues, List<CandidateItem> childCandidates, List<FormulaSlot> slots, int parentId, List<MatchEntry> results, int sourceType = 0)
        {
            if (parentValues == null || parentValues.Count == 0)
            {
                Log.Warning("[TryMatch] ParentValues null/empty. ParentID={ParentID}", parentId);
                return;
            }
            if (childCandidates == null || childCandidates.Count == 0)
            {
                Log.Warning("[TryMatch] No child candidates. ParentID={ParentID}", parentId);
                return;
            }
            if (slots == null || slots.Count == 0)
            {
                Log.Warning("[TryMatch] Slots null/empty. ParentID={ParentID}", parentId);
                return;
            }
            results ??= new List<MatchEntry>();
            try
            {
                bool isKQ = sheetCode?.Contains("KQ", StringComparison.OrdinalIgnoreCase) == true;

                int plusQuota = slots.Count(s => s.Sign == 1);
                int minusQuota = slots.Count(s => s.Sign == -1);

                bool isAllPlus = plusQuota > 0 && minusQuota == 0;
                bool isAllMinus = plusQuota == 0 && minusQuota > 0;

                // isPast=false → index 0, 2 (hiện tại)
                // isPast=true  → index 1, 3 (quá khứ)
                var keysToCheck = parentValues.Keys
                    .Where((key, index) => isPast ? (index == 1 || index == 3) : (index == 0 || index == 2))
                    .ToList();

                if (keysToCheck.Count == 0)
                {
                    Log.Warning("[TryMatch] No keysToCheck for isPast={IsPast}. ParentID={ParentID}", isPast, parentId);
                    return;
                }

                int kMax = Math.Min(childCandidates.Count, slots.Count);
                bool stop = false;
                for (int k = 1; k <= kMax; k++)
                {
                    if (stop) break;
                    foreach (var cs in ChooseCandidates(childCandidates, k))
                    {
                        if (stop) break;

                        if (isAllPlus) // Toàn cộng
                        {
                            bool ok = false;
                            int flipMax = isKQ ? (1 << k) : 1;

                            for (int flipMask = 0; flipMask < flipMax && !ok; flipMask++)
                            {
                                bool match = true;

                                foreach (var year in keysToCheck)
                                {
                                    decimal sum = 0;

                                    for (int i = 0; i < k; i++)
                                    {
                                        if (!cs[i].Values.TryGetValue(year, out var v) || !v.HasValue)
                                            continue;

                                        var val = v.Value;

                                        if (isKQ && ((flipMask & (1 << i)) != 0))
                                            val = -val;

                                        sum += val;
                                    }

                                    if (Math.Abs(sum - (parentValues[year] ?? 0)) > 1)
                                    {
                                        match = false;
                                        break;
                                    }
                                }

                                if (match)
                                    ok = true;
                            }

                            if (ok)
                            {
                                if (k >= 5)
                                    stop = true;

                                results.Add(new MatchEntry { ParentId = parentId, SourceType = sourceType, Candidates = cs.ToList() });
                                break;
                            }
                        }
                        else if (isAllMinus) // Toàn trừ
                        {
                            bool ok = false;
                            int flipMax = isKQ ? (1 << k) : 1;

                            for (int flipMask = 0; flipMask < flipMax && !ok; flipMask++)
                            {
                                bool match = true;

                                foreach (var year in keysToCheck)
                                {
                                    decimal sum = 0;

                                    for (int i = 0; i < k; i++)
                                    {
                                        if (!cs[i].Values.TryGetValue(year, out var v) || !v.HasValue)
                                            continue;

                                        var val = v.Value;
                                        val = -val;

                                        if (isKQ && ((flipMask & (1 << i)) != 0))
                                            val = -val;

                                        sum += val;
                                    }

                                    if (Math.Abs(sum - (parentValues[year] ?? 0)) > 1)
                                    {
                                        match = false;
                                        break;
                                    }
                                }

                                if (match)
                                    ok = true;
                            }

                            if (ok)
                            {
                                if (k >= 5)
                                    stop = true;

                                results.Add(new MatchEntry { ParentId = parentId, SourceType = sourceType, Candidates = cs.ToList() });
                                break;
                            }
                        }
                        else // Có cộng có trừ
                        {
                            int maskMax = 1 << k;
                            int flipMax = isKQ ? (1 << k) : 1;

                            for (int mask = 0; mask < maskMax; mask++)
                            {
                                int plus = 0, minus = 0;

                                for (int i = 0; i < k; i++)
                                    if ((mask & (1 << i)) != 0) plus++;
                                    else minus++;

                                if (plus > plusQuota || minus > minusQuota)
                                    continue;

                                for (int flipMask = 0; flipMask < flipMax; flipMask++)
                                {
                                    bool ok = true;

                                    foreach (var year in keysToCheck)
                                    {
                                        decimal sum = 0;

                                        for (int i = 0; i < k; i++)
                                        {
                                            if (!cs[i].Values.TryGetValue(year, out var v) || !v.HasValue)
                                                continue;

                                            var val = ((mask & (1 << i)) != 0) ? v.Value : -v.Value;

                                            if (isKQ && ((flipMask & (1 << i)) != 0))
                                                val = -val;

                                            sum += val;
                                        }

                                        if (Math.Abs(sum - (parentValues[year] ?? 0)) > 1)
                                        {
                                            ok = false;
                                            break;
                                        }
                                    }

                                    if (ok)
                                    {
                                        if (k >= 5)
                                            stop = true;

                                        results.Add(new MatchEntry { ParentId = parentId, SourceType = sourceType, Candidates = cs.ToList() });

                                        goto END_MASK_LOOP;
                                    }
                                }
                            }

                        END_MASK_LOOP:;
                        }
                    }
                }
            }
            catch (ArgumentNullException ex)
            {
                Log.Error(ex, "[TryMatch][ARG_NULL] ParentID={ParentID} Param={Param}", parentId, ex.ParamName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[TryMatch][UNEXPECTED] ParentID={ParentID}", parentId);
            }
        }
        private CandidateItem? FindItemInModel(FinancialReportModel model, string sheetCode, int id)
        {
            string shortCode = sheetCode.Split('_').Last();

            List<FinancialReportItem>? sheet = shortCode switch
            {
                "CD" => model.BalanceSheet,
                "KQ" => model.IncomeStatement,
                "LCGT" or "LCTT" => model.CashFlow,
                "NB" => model.OffBalanceSheet,
                _ => null
            };

            if (sheet == null)
                return null;

            var item = sheet.FirstOrDefault(x => x.ReportNormID == id);
            if (item == null)
                return null;

            return new CandidateItem
            {
                ReportNormID = item.ReportNormID,
                SheetCode = shortCode,
                ScanIndex = item.ScanIndex,
                Name = item.Item ?? string.Empty,
                Values = item.Values != null ? new Dictionary<string, decimal?>(item.Values) : new Dictionary<string, decimal?>()
            };
        }
        public static List<FormulaSlot> ParseSlots(string formula)
        {
            try
            {
                var list = new List<FormulaSlot>();
                var ms = Regex.Matches(formula, @"([+-]?)\s*@(\d+)");

                foreach (Match m in ms)
                {
                    list.Add(new FormulaSlot
                    {
                        ChildID = int.Parse(m.Groups[2].Value),
                        Sign = m.Groups[1].Value == "-" ? -1 : 1
                    });
                }

                return list;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[AUTO_REMAP][PARSE_SLOT_FAIL] Formula={Formula}", formula);
                return new List<FormulaSlot>();
            }
        }
        List<List<CandidateItem>> ChooseCandidates(List<CandidateItem> cands, int k)
        {
            var res = new List<List<CandidateItem>>();
            void dfs(int i, List<CandidateItem> cur)
            {
                if (cur.Count == k) { res.Add(new List<CandidateItem>(cur)); return; }
                for (; i < cands.Count; i++)
                {
                    cur.Add(cands[i]);
                    dfs(i + 1, cur);
                    cur.RemoveAt(cur.Count - 1);
                }
            }
            dfs(0, new List<CandidateItem>());
            return res;
        }
        FormulaCandidate BuildResult(FormulaError error, List<MatchEntry> rs, List<int> IDDefaults)
        {
            var fc = new FormulaCandidate
            {
                SheetCode = error.SheetCode,
                ParentID = error.Parent.ID,
                Formula = error.Formula
            };

            var childIdsInFormula = ParseSlots(error.Formula).Select(s => s.ChildID).Distinct().Where(id => !IDDefaults.Contains(id)).ToList();

            // Group theo ParentID trước
            var entriesForParent = rs.Where(e => e.ParentId == error.Parent.ID && e.Candidates != null && e.Candidates.Count > 0).ToList();

            if (entriesForParent.Count == 0)
            {
                fc.Status = CandidateStatus.NO_SOLUTION_KEEP_ORIGINAL;
                fc.Reason = "no match";
                fc.MatchedCombinations = new List<CandidateChoose>();
                return fc;
            }

            // ChildPDFs: List<CandidateSource>
            // Mỗi entry là 1 tổ hợp, cùng Source thì Index tăng dần từ 1
            var childPDFs = new List<CandidateSource>();

            // Đếm index theo từng sourceType
            var indexCounters = new Dictionary<int, int>();

            foreach (var entry in entriesForParent)
            {
                // Kiểm tra duplicate (cùng sourceType, cùng ScanIndex)
                bool isDuplicate = childPDFs.Where(x => x.Source == entry.SourceType)
                                            .Any(x => x.CandidateItems.Count == entry.Candidates.Count && x.CandidateItems.Zip(entry.Candidates, (a, b) => a.ScanIndex == b.ScanIndex).All(m => m));

                if (isDuplicate)
                    continue;

                if (!indexCounters.ContainsKey(entry.SourceType))
                    indexCounters[entry.SourceType] = 0;

                indexCounters[entry.SourceType]++;

                childPDFs.Add(new CandidateSource
                {
                    Source = entry.SourceType,
                    Index = indexCounters[entry.SourceType],
                    CandidateItems = entry.Candidates.ToList()
                });
            }

            if (childPDFs.Count == 0)
            {
                fc.Status = CandidateStatus.NO_SOLUTION_KEEP_ORIGINAL;
                fc.Reason = "no match";
                fc.MatchedCombinations = new List<CandidateChoose>();
                return fc;
            }

            var choose = new CandidateChoose
            {
                ParentID = error.Parent.ID,
                ParentPDF = new List<CandidateItem>(),
                ChildIDs = childIdsInFormula,
                ChildPDFs = childPDFs
            };

            fc.MatchedCombinations = new List<CandidateChoose> { choose };
            fc.Status = CandidateStatus.NEED_AI_CHOICE;

            return fc;
        }
        //FormulaCandidate BuildResult(FormulaError error, List<Dictionary<int, List<CandidateItem>>> rs, List<int> IDDefaults)
        //{
        //    var fc = new FormulaCandidate
        //    {
        //        SheetCode = error.SheetCode,
        //        ParentID = error.Parent.ID,
        //        Formula = error.Formula
        //    };

        //    // ChildIDs từ formula
        //    var childIdsInFormula = ParseSlots(error.Formula).Select(s => s.ChildID).Distinct().Where(id => !IDDefaults.Contains(id)).ToList();

        //    // ===== GỘP THEO ParentID =====
        //    var grouped = rs.Where(m => m.ContainsKey(error.Parent.ID)).Select(m => m[error.Parent.ID]).Where(list => list != null && list.Count > 0).ToList();

        //    if (grouped.Count == 0)
        //    {
        //        fc.Status = CandidateStatus.NO_SOLUTION_KEEP_ORIGINAL;
        //        fc.Reason = "no match";
        //        fc.MatchedCombinations = new List<CandidateChoose>();
        //        return fc;
        //    }

        //    // Parent PDF không nằm trong TryMatch → để trống hoặc xử lý sau
        //    var parentPDF = new List<CandidateItem>();

        //    // ===== ChildPDFs: index chỉ là thứ tự =====
        //    var childPDFs = new Dictionary<int, List<CandidateItem>>();
        //    int idx = 0;

        //    foreach (var childList in grouped)
        //    {
        //        bool isDuplicate = childPDFs.Values.Any(existing => existing.Count == childList.Count && existing.Zip(childList, (a, b) => a.ScanIndex == b.ScanIndex).All(match => match));

        //        if (!isDuplicate)
        //        {
        //            childPDFs[idx++] = childList.ToList();
        //        }
        //    }
        //    var choose = new CandidateChoose
        //    {
        //        ParentID = error.Parent.ID,
        //        ParentPDF = parentPDF,
        //        ChildIDs = childIdsInFormula,
        //        ChildPDFs = childPDFs
        //    };

        //    fc.MatchedCombinations = new List<CandidateChoose> { choose };
        //    fc.Status = CandidateStatus.NEED_AI_CHOICE;

        //    return fc;
        //}

    }
}
