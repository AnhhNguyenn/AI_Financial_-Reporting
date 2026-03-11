using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Enum;
using MappingReportNorm.Extensions;
using MappingReportNorm.Interfaces.Services;
using MappingReportNorm.Utils;
using MappingReportNorm.Utils.ScanDataParser;
using MappingReportNorm.Utils.ScanDataParser.Models;

namespace MappingReportNorm.Services
{
    public class NormalizationService : INormalizationService
    {
        private readonly IFinancialService _financialService;
        public NormalizationService
        (
            IFinancialService financialService
        )
        {
            _financialService = financialService;
        }

        public async Task<List<ReportNorm>> ReportNorms_Database(ReportTemplate reportTemplate, ReportComponentType reportComponentType)
        {
            var allReportNorms = await _financialService.GetAllReportNormsAsync();

            // 1. BALANCE SHEET
            if (reportComponentType == ReportComponentType.BalanceSheet)
            {
                int[] excludedIds;
                if (reportTemplate == ReportTemplate.JointStock)
                {
                    excludedIds = new[] { 2996, 3000, 3001, 2997, 2998, 2999 };
                }
                else if (reportTemplate == ReportTemplate.Bank)
                {
                    excludedIds = new[] { 4375, 4305, 4304 };
                }
                else if (reportTemplate == ReportTemplate.Securities)
                {
                    excludedIds = new[] { 4476, 4479, 4480, 4481, 4477, 4478 };
                }
                else
                {
                    excludedIds = new[] { 3095, 3098, 3099, 3100, 3096, 3097 };
                }

                var reportNorms_Excluded = allReportNorms
                        .Where(item =>
                            !excludedIds.Contains(item.ReportNormID) &&
                            item.ReportTemplateID == (int)reportTemplate &&
                            item.ReportComponentTypeID == (int)reportComponentType)
                        .OrderBy(item => item.Ordering)
                        .ToList();

                var reportNorms_Final = FullPathBuilder.BuildDynamic(
                    reportNorms_Excluded,
                    idField: "ReportNormID",
                    parentIdField: "ParentReportNormID",
                    nameField: "Name",
                    fullPathField: "FullPathName",
                    separator: " > "
                );

                foreach (var itemNorm in reportNorms_Final)
                {
                    ReportNorm? findParent = null;
                    if (itemNorm.ParentReportNormID != 0 && itemNorm.ParentReportNormID != itemNorm.ReportNormID)
                    {
                        findParent = reportNorms_Final.Find(item => item.ReportNormID == itemNorm.ParentReportNormID);
                    }
                    string parentName = findParent != null ? findParent.Name : null;
                    itemNorm.ParentName = parentName;
                }

                var reportNorms_Included = allReportNorms
                    .Where(item => excludedIds.Contains(item.ReportNormID));
                foreach (var item in reportNorms_Included)
                {
                    item.FullPathName = item.Name;
                }
                reportNorms_Final.AddRange(reportNorms_Included);

                reportNorms_Final = reportNorms_Final
                    .OrderByHierarchy(
                        item => item.ParentReportNormID,
                        item => item.ReportNormID,
                        item => item.Ordering
                ).ToList();
                return reportNorms_Final;
            }
            // 2. KQKD (Income) + LCTT Gián tiếp/Trực tiếp (CashFlow Indirect/Direct)
            // Sửa đoạn này: Check cụ thể từng Enum
            else if (reportComponentType == ReportComponentType.IncomeStatement ||
                     reportComponentType == ReportComponentType.CashFlowIndirect ||
                     reportComponentType == ReportComponentType.CashFlowDirect)
            {
                // Logic y hệt Balance Sheet (xây dựng cây full path) nhưng không có excludedIds
                int[] excludedIds = new int[] { };

                var reportNorms_Excluded = allReportNorms
                        .Where(item =>
                            !excludedIds.Contains(item.ReportNormID) &&
                            item.ReportTemplateID == (int)reportTemplate &&
                            item.ReportComponentTypeID == (int)reportComponentType) // Tự động lấy đúng ID của loại đang chạy
                        .OrderBy(item => item.Ordering)
                        .ToList();

                var reportNorms_Final = FullPathBuilder.BuildDynamic(
                    reportNorms_Excluded,
                    idField: "ReportNormID",
                    parentIdField: "ParentReportNormID",
                    nameField: "Name",
                    fullPathField: "FullPathName",
                    separator: " > "
                );

                foreach (var itemNorm in reportNorms_Final)
                {
                    ReportNorm? findParent = null;
                    if (itemNorm.ParentReportNormID != 0 && itemNorm.ParentReportNormID != itemNorm.ReportNormID)
                    {
                        findParent = reportNorms_Final.Find(item => item.ReportNormID == itemNorm.ParentReportNormID);
                    }
                    string parentName = findParent != null ? findParent.Name : null;
                    itemNorm.ParentName = parentName;
                }

                var reportNorms_Included = allReportNorms
                    .Where(item => excludedIds.Contains(item.ReportNormID));
                foreach (var item in reportNorms_Included)
                {
                    item.FullPathName = item.Name;
                }
                reportNorms_Final.AddRange(reportNorms_Included);

                reportNorms_Final = reportNorms_Final
                    .OrderByHierarchy(
                        item => item.ParentReportNormID,
                        item => item.ReportNormID,
                        item => item.Ordering
                ).ToList();
                return reportNorms_Final;
            }
            // 3. OFF BALANCE SHEET (Giữ nguyên logic cũ của bạn)
            else if (reportComponentType == ReportComponentType.OffBalanceSheet)
            {
                var reportNorms_Final = allReportNorms
                    .Where(item =>
                        item.ReportTemplateID == (int)reportTemplate &&
                        item.ReportComponentTypeID == (int)reportComponentType)
                    .OrderByHierarchy(
                        item => item.ParentReportNormID,
                        item => item.ReportNormID,
                        item => item.Ordering
                ).ToList();

                foreach (var itemNorm in reportNorms_Final)
                {
                    ReportNorm? findParent = null;
                    if (itemNorm.ParentReportNormID != 0 && itemNorm.ParentReportNormID != itemNorm.ReportNormID)
                    {
                        findParent = reportNorms_Final.Find(item => item.ReportNormID == itemNorm.ParentReportNormID);
                    }
                    string parentName = findParent != null ? findParent.Name : null;
                    itemNorm.ParentName = parentName;
                }

                return reportNorms_Final;
            }
            // 4. Default
            else
            {
                var reportNorms_Final = allReportNorms
                    .Where(item =>
                        item.ReportTemplateID == (int)reportTemplate &&
                        item.ReportComponentTypeID == (int)reportComponentType)
                    .OrderBy(item => item.Ordering)
                    .ToList();
                foreach (var item in reportNorms_Final)
                {
                    item.FullPathName = item.Name;
                }
                return reportNorms_Final;
            }
        }

        public async Task<List<ReportNorm>> Chunking_ReportNorms_Database(ReportTemplate reportTemplate, ReportComponentType reportComponentType, int chunk)
        {
            var data = await ReportNorms_Database(reportTemplate, reportComponentType);
            return data;
        }

        public async Task<List<TreeNode>> ReportNorms_Scan(List<ScanItem> list, ReportTemplate reportTemplate, ReportComponentType reportComponentType)
        {
            var parser = new TreeParser();
            var treeNodes = parser.Parse(list);

            // 1. SCAN - BALANCE SHEET
            if (reportComponentType == ReportComponentType.BalanceSheet)
            {
                int[] excludedIds;
                if (reportTemplate == ReportTemplate.JointStock)
                {
                    excludedIds = new[] { 2996, 3000, 3001, 2997, 2998, 2999 };
                }
                else if (reportTemplate == ReportTemplate.Bank)
                {
                    excludedIds = new[] { 4375, 4305, 4304 };
                }
                else if (reportTemplate == ReportTemplate.Securities)
                {
                    excludedIds = new[] { 4476, 4479, 4480, 4481, 4477, 4478 };
                }
                else
                {
                    excludedIds = new[] { 3095, 3098, 3099, 3100, 3096, 3097 };
                }
                var reportNorms_Excluded = treeNodes
                    .Where(item =>
                        !item.ReportNormID.HasValue || !excludedIds.Contains(item.ReportNormID.Value))
                    .OrderBy(item => item.Index)
                    .ToList();

                var result = FullPathBuilder.BuildDynamic(
                    reportNorms_Excluded,
                    idField: "Index",
                    parentIdField: "ParentIndex",
                    nameField: "Text",
                    fullPathField: "FullPathText",
                    separator: " > "
                );

                foreach (var itemResult in result)
                {
                    TreeNode? findParent = null;
                    if (itemResult.ParentIndex != 0 && itemResult.ParentIndex != itemResult.Index)
                    {
                        findParent = result.Find(item => item.Index == itemResult.ParentIndex);
                    }
                    string parentText = !string.IsNullOrWhiteSpace(itemResult.ParentText) ? itemResult.ParentText : findParent != null ? findParent.Text : null;
                    itemResult.ParentText = parentText;
                }

                var reportNorms_Included = treeNodes
                    .Where(item => item.ReportNormID.HasValue && excludedIds.Contains(item.ReportNormID.Value)).ToList();
                foreach (var item in reportNorms_Included)
                {
                    item.FullPathText = item.Text;
                }
                result.AddRange(reportNorms_Included);

                result = result.OrderBy(item => item.Index).ToList();

                return result;
            }
            // 2. SCAN - KQKD & LCTT (INDIRECT / DIRECT)
            else if (reportComponentType == ReportComponentType.IncomeStatement ||
                     reportComponentType == ReportComponentType.CashFlowIndirect ||
                     reportComponentType == ReportComponentType.CashFlowDirect)
            {
                int[] excludedIds = new int[] { }; // Rỗng

                var reportNorms_Excluded = treeNodes
                    .Where(item =>
                        !item.ReportNormID.HasValue || !excludedIds.Contains(item.ReportNormID.Value))
                    .OrderBy(item => item.Index)
                    .ToList();

                // Build Full Path
                var result = FullPathBuilder.BuildDynamic(
                    reportNorms_Excluded,
                    idField: "Index",
                    parentIdField: "ParentIndex",
                    nameField: "Text",
                    fullPathField: "FullPathText",
                    separator: " > "
                );

                foreach (var itemResult in result)
                {
                    TreeNode? findParent = null;
                    if (itemResult.ParentIndex != 0 && itemResult.ParentIndex != itemResult.Index)
                    {
                        findParent = result.Find(item => item.Index == itemResult.ParentIndex);
                    }
                    string parentText = !string.IsNullOrWhiteSpace(itemResult.ParentText) ? itemResult.ParentText : findParent != null ? findParent.Text : null;
                    itemResult.ParentText = parentText;
                }

                var reportNorms_Included = treeNodes
                    .Where(item => item.ReportNormID.HasValue && excludedIds.Contains(item.ReportNormID.Value)).ToList();
                foreach (var item in reportNorms_Included)
                {
                    item.FullPathText = item.Text;
                }
                result.AddRange(reportNorms_Included);

                result = result.OrderBy(item => item.Index).ToList();

                return result;
            }
            // 3. SCAN - OFF BALANCE SHEET
            else if (reportComponentType == ReportComponentType.OffBalanceSheet)
            {
                var result = treeNodes.OrderBy(item => item.Index).ToList();
                foreach (var itemResult in result)
                {
                    TreeNode? findParent = null;
                    if (itemResult.ParentIndex != 0 && itemResult.ParentIndex != itemResult.Index)
                    {
                        findParent = result.Find(item => item.Index == itemResult.ParentIndex);
                    }
                    string parentText = !string.IsNullOrWhiteSpace(itemResult.ParentText) ? itemResult.ParentText : findParent != null ? findParent.Text : null;
                    itemResult.ParentText = parentText;
                }
                return result;
            }
            // 4. Default
            else
            {
                var result = treeNodes.OrderBy(item => item.Index).ToList();
                foreach (var item in result)
                {
                    item.FullPathText = item.Text;
                    item.ParentText = null;
                }
                return result;
            }
        }
    }
}