using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BCTC.DataAccess.Models;
using MappingReportNorm.Models;
using MappingReportNorm.Services;
using Serilog;

namespace BCTC.App.Utils
{
    public static class RecheckAutoAddHelper
    {
        public static void DeduplicateReportNormIDs(FinancialReportModel model)
        {
            try
            {
                if (model == null) return;

                bool isCashFlowIndirect = string.Equals(model.CashFlowMethod, "indirect", StringComparison.OrdinalIgnoreCase);

                var sheets = new Dictionary<string, List<FinancialReportItem>>();
                if (model.BalanceSheet != null) sheets["CD"] = model.BalanceSheet;
                if (model.IncomeStatement != null) sheets["KQ"] = model.IncomeStatement;
                if (model.CashFlow != null) sheets[isCashFlowIndirect ? "LCGT" : "LCTT"] = model.CashFlow;
                if (model.OffBalanceSheet != null) sheets["NB"] = model.OffBalanceSheet;

                foreach (var (sheetCode, sheet) in sheets)
                {
                    var duplicateGroups = sheet.Where(x => x?.ReportNormID != null).GroupBy(x => x.ReportNormID!.Value).Where(g => g.Count() > 1).ToList();

                    foreach (var group in duplicateGroups)
                    {
                        int normId = group.Key;
                        var rows = group.ToList();

                        if (rows.Count == 2)
                        {
                            var r1 = rows[0];
                            var r2 = rows[1];

                            bool valuesIdentical = AreValuesIdentical(r1, r2);
                            bool partialMatch = !valuesIdentical && HasPartialMatch(r1, r2);

                            if (valuesIdentical)
                            {
                                // Giống hoàn toàn -> giữ r1, gán r2 = null
                                r2.ReportNormID = null;
                                Log.Information("[Dedup][{Sheet}] ID={Id} trùng 2 row, value giống nhau → giữ ScanIndex={Keep}, gán null ScanIndex={Null}", sheetCode, normId, r1.ScanIndex, r2.ScanIndex);
                            }
                            else if (partialMatch)
                            {
                                // Giống một phần -> giữ row đầy đủ hơn (nhiều value non-null hơn)
                                int count1 = CountNonNullValues(r1);
                                int count2 = CountNonNullValues(r2);
                                var toKeep = count1 >= count2 ? r1 : r2;
                                var toNullify = count1 >= count2 ? r2 : r1;
                                toNullify.ReportNormID = null;
                                Log.Information("[Dedup][{Sheet}] ID={Id} trùng 2 row, value nửa giống → giữ ScanIndex={Keep}({KeepCount}), gán null ScanIndex={Null}({NullCount})",
                                    sheetCode, normId, toKeep.ScanIndex, count1 >= count2 ? count1 : count2, toNullify.ScanIndex, count1 >= count2 ? count2 : count1);
                            }
                            else
                            {
                                // Khác hoàn toàn -> gán cả 2 = null
                                r1.ReportNormID = null;
                                r2.ReportNormID = null;
                                Log.Information("[Dedup][{Sheet}] ID={Id} trùng 2 row, value khác nhau hoàn toàn → gán cả 2 = null (ScanIndex={S1},{S2})", sheetCode, normId, r1.ScanIndex, r2.ScanIndex);
                            }
                        }
                        else
                        {
                            // Hơn 2 row trùng
                            bool allIdentical = rows.All(r => AreValuesIdentical(r, rows[0]));

                            if (allIdentical)
                            {
                                // Tất cả giống nhau -> giữ row đầu, gán còn lại = null
                                for (int i = 1; i < rows.Count; i++)
                                    rows[i].ReportNormID = null;
                                Log.Information("[Dedup][{Sheet}] ID={Id} trùng {Count} row, value giống nhau hết → giữ ScanIndex={Keep}, gán còn lại = null", sheetCode, normId, rows.Count, rows[0].ScanIndex);
                            }
                            else
                            {
                                // Có row khác nhau -> gán tất cả = null
                                foreach (var r in rows)
                                    r.ReportNormID = null;
                                Log.Information("[Dedup][{Sheet}] ID={Id} trùng {Count} row, value khác nhau → gán tất cả = null", sheetCode, normId, rows.Count);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DeduplicateReportNormIDs][ERROR]");
            }
        }

        // So sánh 2 row có value giống nhau hoàn toàn không (theo từng key)
        private static bool AreValuesIdentical(FinancialReportItem r1, FinancialReportItem r2)
        {
            if (r1.Values == null && r2.Values == null) return true;
            if (r1.Values == null || r2.Values == null) return false;

            var keys = r1.Values.Keys.Union(r2.Values.Keys).ToList();
            foreach (var key in keys)
            {
                r1.Values.TryGetValue(key, out var v1);
                r2.Values.TryGetValue(key, out var v2);
                if (v1 != v2) return false;
            }
            return true;
        }

        // Kiểm tra 2 row có ít nhất 1 key giống nhau (partial match)
        private static bool HasPartialMatch(FinancialReportItem r1, FinancialReportItem r2)
        {
            if (r1.Values == null || r2.Values == null) return false;

            foreach (var key in r1.Values.Keys)
            {
                var v1 = r1.Values[key];
                if (!r2.Values.TryGetValue(key, out var v2)) continue;

                // Bỏ qua nếu bất kỳ bên nào null hoặc = 0
                bool v1Empty = !v1.HasValue || v1.Value == 0;
                bool v2Empty = !v2.HasValue || v2.Value == 0;
                if (v1Empty || v2Empty) continue;

                if (v1 == v2) return true;
            }
            return false;
        }
        private static int CountNonNullValues(FinancialReportItem r)
        {
            return r.Values?.Values.Count(v => v.HasValue) ?? 0;
        }
        public static void RecheckAutoAdd(FinancialReportModel model, List<FormulaDefinition> allFormulas)
        {
            try
            {
                if (model == null || allFormulas == null)
                    return;

                bool isCashFlowIndirect = string.Equals(model.CashFlowMethod, "indirect", StringComparison.OrdinalIgnoreCase);

                var sheets = new Dictionary<string, List<FinancialReportItem>>();
                if (model.BalanceSheet != null) sheets["CD"] = model.BalanceSheet;
                if (model.IncomeStatement != null) sheets["KQ"] = model.IncomeStatement;
                if (model.CashFlow != null) sheets[isCashFlowIndirect ? "LCGT" : "LCTT"] = model.CashFlow;
                if (model.OffBalanceSheet != null) sheets["NB"] = model.OffBalanceSheet;

                var autoAddItems = new List<(string SheetCode, FinancialReportItem Item)>();

                foreach (var (code, sheet) in sheets)
                {
                    foreach (var item in sheet)
                    {
                        if (item?.ReportNormID == null || item.Values == null)
                            continue;

                        bool isItemEmpty = string.IsNullOrWhiteSpace(item.Item);
                        if (isItemEmpty)
                            autoAddItems.Add((code, item));
                    }
                }

                if (autoAddItems.Count == 0)
                    return;

                // ── 3. Xây dựng lookup từ formulas
                var childRegex = new Regex(@"@(\d+)", RegexOptions.Compiled);

                // parentId → formula định nghĩa nó là cha
                var parentToFormula = new Dictionary<int, FormulaDefinition>();
                // childId  → danh sách formula mà nó là con
                var childToFormulas = new Dictionary<int, List<FormulaDefinition>>();

                foreach (var f in allFormulas.Where(x => x.SheetCode != "SPECIAL" && x.ReportNormID.HasValue))
                {
                    if (string.IsNullOrWhiteSpace(f.Formula)) continue;

                    parentToFormula[f.ReportNormID!.Value] = f;

                    var childIds = childRegex.Matches(f.Formula).Select(m => int.Parse(m.Groups[1].Value)).Distinct();

                    foreach (var childId in childIds)
                    {
                        if (!childToFormulas.ContainsKey(childId))
                            childToFormulas[childId] = new List<FormulaDefinition>();
                        childToFormulas[childId].Add(f);
                    }
                }

                // ── 4. Helper: lấy toàn bộ ReportNormID đang tồn tại trong model ───────────
                HashSet<int> GetAllExistingIds() => sheets.Values.SelectMany(s => s).Where(x => x?.ReportNormID != null && !string.IsNullOrWhiteSpace(x.Item)).Select(x => x.ReportNormID!.Value).ToHashSet();

                // ── 5. Helper: kiểm tra sheet match ─────────────────────────────────────────
                static bool SheetMatch(string itemSheet, string formulaSheet) => !string.IsNullOrEmpty(formulaSheet) && (itemSheet.Contains(formulaSheet) || formulaSheet.Contains(itemSheet));

                // ── 6. Duyệt từng item null-value và quyết định remove ───────────────────────
                var toRemove = new HashSet<(string SheetCode, int ReportNormID)>();

                foreach (var (sheetCode, item) in autoAddItems)
                {
                    if (item.ReportNormID == null) continue;
                    int id = item.ReportNormID.Value;

                    // Bỏ qua nếu đã được đánh dấu remove ở vòng trước
                    if (toRemove.Contains((sheetCode, id))) continue;

                    bool markedForRemoval = false;

                    // ── CASE 1: ID là CHA trong công thức ───────────────────────────────────
                    if (!markedForRemoval && parentToFormula.TryGetValue(id, out var parentFormula))
                    {
                        // Chỉ xử lý nếu formula thuộc cùng sheet
                        if (SheetMatch(sheetCode, parentFormula.SheetCode))
                        {
                            var childIds = childRegex.Matches(parentFormula.Formula).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();

                            if (childIds.Count > 0)
                            {
                                int firstChildId = childIds[0];
                                var existingIds = GetAllExistingIds();

                                if (!existingIds.Contains(firstChildId))
                                {
                                    toRemove.Add((sheetCode, id));
                                    markedForRemoval = true;
                                    Log.Information("[RecheckAutoAdd] Remove PARENT ID={Id} Sheet={Sheet} — " + "first child @{ChildId} not found in model", id, sheetCode, firstChildId);
                                }
                            }
                        }
                    }

                    // ── CASE 2: ID là CON trong công thức ───────────────────────────────────
                    if (!markedForRemoval && childToFormulas.TryGetValue(id, out var parentFormulas))
                    {
                        // Lấy formula khớp sheet đầu tiên (thường chỉ có 1)
                        var matchedFormula = parentFormulas.FirstOrDefault(f => SheetMatch(sheetCode, f.SheetCode));

                        if (matchedFormula?.ReportNormID != null)
                        {
                            int parentId = matchedFormula.ReportNormID.Value;
                            var existingIds = GetAllExistingIds();

                            if (!existingIds.Contains(parentId))
                            {
                                toRemove.Add((sheetCode, id));
                                markedForRemoval = true;
                                Log.Information("[RecheckAutoAdd] Remove CHILD ID={Id} Sheet={Sheet} — " + "parent @{ParentId} not found in model", id, sheetCode, parentId);
                            }
                        }
                    }
                }

                // ── 7. Thực hiện remove ──────────────────────────────────────────────────────
                foreach (var (sheetCode, idToRemove) in toRemove)
                {
                    // Tìm sheet khớp (key có thể là "LCGT"/"LCTT" trong khi sheetCode là "LCGT")
                    var matchedSheet = sheets
                        .Where(kv => SheetMatch(sheetCode, kv.Key))
                        .Select(kv => kv.Value)
                        .FirstOrDefault();

                    if (matchedSheet == null) continue;

                    int removed = matchedSheet.RemoveAll(x => x?.ReportNormID == idToRemove);
                    if (removed > 0)
                        Log.Information("[RecheckAutoAdd] Removed {Count} row(s) ReportNormID={Id} from Sheet={Sheet}",removed, idToRemove, sheetCode);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RecheckAutoAdd][ERROR]");
            }
        }
    }
    public static class RemapApplyHelper
    {
        /// <summary>
        /// Trả về true nếu có ít nhất 1 thay đổi được áp dụng.
        /// </summary>
        public static bool ApplyRemapResult(ReMappingResponse remapResult, FinancialReportModel model, List<FormulaDefinition> allFormulas)
        {
            if (remapResult?.Groups == null || remapResult.Groups.Count == 0)
                return false;

            bool hasAnyUpdate = false;

            // Build formula lookup: parentId → FormulaDefinition
            var formulaLookup = allFormulas
            .Where(f => f.ReportNormID.HasValue && !string.IsNullOrWhiteSpace(f.Formula) && !string.Equals(f.SheetCode, "SPECIAL", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.ReportNormID!.Value)
            .ToDictionary(g => g.Key, g => g.First());

            foreach (var group in remapResult.Groups)
            {
                try
                {
                    // Bỏ group rỗng (đã bị clear bởi logic cha-con)
                    if (group.Mappings == null || group.Mappings.Count == 0)
                        continue;

                    // ── 1. Lấy ChildIDs từ công thức ──────────────────────────────────────
                    if (!formulaLookup.TryGetValue(group.ParentID, out var formulaDef))
                        continue;

                    var sheet = GetSheet(model, formulaDef.SheetCode);
                    if (sheet == null) continue;

                    

                    var slots = FormulaAutoRemap.ParseSlots(formulaDef.Formula);
                    // ChildIDs theo công thức (trừ default IDs đã được giữ cố định)
                    var formulaChildIds = slots.Select(s => s.ChildID).ToHashSet();
                    var slotSignMap = slots.ToDictionary(s => s.ChildID, s => s.Sign);

                    // ── 2. ChildIDs AI trả về: ScanIndex → newReportNormID ─────────────────
                    // Chỉ lấy các mapping thuộc đúng sheet của group
                    var aiMappings = group.Mappings.Where(m => !string.IsNullOrEmpty(m.SheetCode)).ToList();

                    var aiScanToId = aiMappings.ToDictionary(m => m.ScanIndex, m => m.ReportNormID);
                    var aiNewIds = aiMappings.Where(m => m.ReportNormID != 0).Select(m => m.ReportNormID).ToHashSet();

                    // ── 3. Cập nhật từng remapItem ─────────────────────────────────────────
                    foreach (var remapItem in aiMappings)
                    {
                        var targetSheet = GetSheet(model, remapItem.SheetCode);
                        if (targetSheet == null) continue;

                        var row = targetSheet.FirstOrDefault(x => x.ScanIndex == remapItem.ScanIndex);
                        if (row == null || remapItem.ScanIndex == 0) continue;

                        int newId = remapItem.ReportNormID;

                        if (newId == 0)
                        {
                            // AI trả 0 → row này không thuộc formula → set null
                            if (row.ReportNormID.HasValue)
                            {
                                Log.Information("[REMAP_APPLY] {Sheet} ScanIndex={SI}: ID={Old} → null (AI trả 0)", remapItem.SheetCode, remapItem.ScanIndex, row.ReportNormID);
                                row.ReportNormID = null;
                                hasAnyUpdate = true;
                            }
                            continue;
                        }

                        // Nếu ID mới đang gán cho row khác → clear row đó trước
                        var conflictRow = targetSheet.FirstOrDefault(x => x.ReportNormID == newId && x.ScanIndex != remapItem.ScanIndex);
                        if (conflictRow != null)
                        {
                            Log.Information("[REMAP_APPLY] {Sheet} ScanIndex={SI}: Clear conflict ID={Id} từ ScanIndex={OldSI}", remapItem.SheetCode, remapItem.ScanIndex, newId, conflictRow.ScanIndex);
                            conflictRow.ReportNormID = null;
                        }

                        int oldId = row.ReportNormID ?? 0;
                        if (oldId == newId) continue;

                        row.ReportNormID = newId;
                        hasAnyUpdate = true;
                        Log.Information("[REMAP_APPLY] {Sheet} ScanIndex={SI}: {Old} → {New}", remapItem.SheetCode, remapItem.ScanIndex, oldId, newId);
                    }

                    // ── 4. Child dư: trong formula nhưng AI không map → set null ───────────
                    // Các ID trong formula mà AI không trả về
                    var missingFromAi = formulaChildIds.Except(aiNewIds).ToList();

                    foreach (var excessId in missingFromAi)
                    {
                        var excessRow = sheet.FirstOrDefault(x => x.ReportNormID == excessId);
                        if (excessRow != null)
                        {
                            Log.Information("[REMAP_APPLY] {Sheet} ScanIndex={SI}: Child dư ID={Id} → null", formulaDef.SheetCode, excessRow.ScanIndex, excessId);
                            excessRow.ReportNormID = null;
                            hasAnyUpdate = true;
                        }
                    }

                    // ── 5. Tính lại value parent theo sum công thức cho các sheet khác KQ
                    if (formulaDef.SheetCode?.Contains("KQ") == true) continue;

                    var parentRow = sheet.FirstOrDefault(x => x.ReportNormID == group.ParentID);
                    if (parentRow?.Values == null) continue;

                    // Lấy lại tất cả child hiện tại trong sheet sau khi đã cập nhật
                    var updatedChildIds = aiNewIds.Where(id => id != 0).ToList();

                    var sumByKey = new Dictionary<string, decimal>();
                    bool anyChildValue = false;

                    foreach (var childId in formulaChildIds)
                    {
                        var childRow = sheet.FirstOrDefault(x => x.ReportNormID == childId);
                        if (childRow?.Values == null) continue;

                        int sign = slotSignMap.TryGetValue(childId, out var s) ? s : 1;

                        foreach (var kv in childRow.Values)
                        {
                            if (!kv.Value.HasValue) continue;

                            if (!sumByKey.ContainsKey(kv.Key))
                                sumByKey[kv.Key] = 0;

                            sumByKey[kv.Key] += sign * kv.Value.Value;
                            anyChildValue = true;
                        }
                    }

                    if (!anyChildValue) continue;

                    // Chỉ cập nhật nếu có sự khác biệt
                    bool parentChanged = false;
                    foreach (var kv in sumByKey)
                    {
                        parentRow.Values.TryGetValue(kv.Key, out var oldVal);
                        if (oldVal != (decimal?)kv.Value)
                        {
                            parentRow.Values[kv.Key] = kv.Value;
                            parentChanged = true;
                        }
                    }

                    if (parentChanged)
                    {
                        hasAnyUpdate = true;
                        var valStr = string.Join(", ", sumByKey.Select(kv => $"{kv.Key}={kv.Value}"));
                        Log.Information("[REMAP_APPLY] ParentID={PID} recalculated after remap. Values: {Vals}", group.ParentID, valStr);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[REMAP_APPLY][ERROR] ParentID={PID}", group.ParentID);
                }
            }

            return hasAnyUpdate;
        }
        public static (bool hasAnyUpdate, HashSet<int> parentsToClear) FilterParentChildConflict(ReMappingResponse remapResult, List<FormulaCandidate> candidates, ResultCheckFormula formula, FinancialReportModel model)
        {
            bool hasAnyUpdate = false;
            var updatedParentIds = new HashSet<int>();
            var parentsToClear = new HashSet<int>();

            var aiReturnedRows = remapResult.Groups.SelectMany(g => g.Mappings).Select(m => (m.SheetCode, m.ScanIndex)).ToHashSet();

            foreach (var group in remapResult.Groups)
            {
                var cand = candidates.FirstOrDefault(c => c.ParentID == group.ParentID);
                if (cand == null) continue;

                var candSheet = GetSheet(model, cand.SheetCode);
                if (candSheet == null) continue;

                foreach (var remapItem in group.Mappings.ToList())
                {
                    var currentRow = candSheet.FirstOrDefault(x => x.ScanIndex == remapItem.ScanIndex);
                    if (currentRow == null) continue;

                    foreach (var otherErr in formula.FormulaErrors.Where(e => e.Parent.ID != cand.ParentID && !updatedParentIds.Contains(e.Parent.ID) && !parentsToClear.Contains(e.Parent.ID)))
                    {
                        var parentSheet = GetSheet(model, otherErr.SheetCode);
                        if (parentSheet == null) continue;

                        var slotSignMap = FormulaAutoRemap.ParseSlots(otherErr.Formula).ToDictionary(s => s.ChildID, s => s.Sign);

                        var otherChildIds = slotSignMap.Keys.ToList();

                        var childRows = parentSheet.Where(x => x.ReportNormID.HasValue && otherChildIds.Contains(x.ReportNormID.Value)).ToList();

                        // Kiểm tra row đang remap có phải là con của công thức lỗi khác không
                        bool isChildOfOtherFormula = childRows.Any(x => x.ScanIndex == currentRow.ScanIndex && otherErr.SheetCode == cand.SheetCode);

                        if (!isChildOfOtherFormula) continue;

                        var siblingsInAi = childRows.Where(x => aiReturnedRows.Contains((otherErr.SheetCode, x.ScanIndex))).ToList();

                        if (siblingsInAi.Count == 0) continue;

                        // Tính lại sum value của parent kia dựa trên các con đã có trong AI result
                        var sumByKey = new Dictionary<string, decimal>();
                        bool anyChildHasValue = false;

                        foreach (var child in siblingsInAi)
                        {
                            if (child.Values == null || !child.ReportNormID.HasValue) continue;

                            int childSign = slotSignMap.TryGetValue(child.ReportNormID.Value, out var s) ? s : 1;

                            foreach (var kv in child.Values)
                            {
                                if (!kv.Value.HasValue) continue;

                                if (!sumByKey.ContainsKey(kv.Key))
                                    sumByKey[kv.Key] = 0;

                                sumByKey[kv.Key] += childSign * kv.Value.Value;
                                anyChildHasValue = true;
                            }
                        }

                        if (!anyChildHasValue) continue;

                        var parentItem = parentSheet.FirstOrDefault(x => x.ReportNormID.HasValue && x.ReportNormID.Value == otherErr.Parent.ID);

                        if (parentItem?.Values != null)
                        {
                            foreach (var kv in sumByKey)
                                parentItem.Values[kv.Key] = kv.Value;

                            updatedParentIds.Add(otherErr.Parent.ID);
                            updatedParentIds.Add(cand.ParentID);
                            parentsToClear.Add(otherErr.Parent.ID);
                            parentsToClear.Add(cand.ParentID);

                            var updatedValues = string.Join(", ", sumByKey.Select(kv => $"{kv.Key}={kv.Value}"));
                            Log.Information("[AI_REMAP_CHECK] ParentID={ParentID} recalculated. Updated values: {UpdatedValues}", otherErr.Parent.ID, updatedValues);

                            hasAnyUpdate = true;
                            break;
                        }
                    }
                }
            }

            // Clear mapping + candidate cho những ParentID bị ảnh hưởng
            foreach (var pid in parentsToClear)
            {
                var g = remapResult.Groups.FirstOrDefault(x => x.ParentID == pid);
                var c = candidates.FirstOrDefault(x => x.ParentID == pid);

                if (g != null)
                {
                    g.Mappings.Clear();
                    Log.Information("[AI_REMAP_CHECK] Cleared mappings of ParentID={ParentID}", pid);
                }
                if (c != null)
                {
                    c.MatchedCombinations = null;
                    c.Status = CandidateStatus.NO_SOLUTION_KEEP_ORIGINAL;
                }
            }

            return (hasAnyUpdate, parentsToClear);
        }
        // Helper: lấy sheet từ model theo sheetCode
        private static List<FinancialReportItem>? GetSheet(FinancialReportModel model, string? sheetCode)
        {
            if (string.IsNullOrEmpty(sheetCode)) return null;
            if (sheetCode.Contains("CD")) return model.BalanceSheet;
            if (sheetCode.Contains("KQ")) return model.IncomeStatement;
            if (sheetCode.Contains("LCGT") || sheetCode.Contains("LCTT")) return model.CashFlow;
            if (sheetCode.Contains("NB")) return model.OffBalanceSheet;
            return null;
        }
    }
}