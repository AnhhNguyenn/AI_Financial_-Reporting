using BCTC.DataAccess.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BCTC.BusinessLogic.OcrLogic
{
    public static class CashFlowMethodResolver
    {
        public static void Resolve(ExtractResult data, int? businessTypeId)
        {
            if (data == null) return;
            if (!string.IsNullOrWhiteSpace(data.CashFlowMethod)) return;
            if (data.CashFlow == null || data.CashFlow.Count == 0) return;

            var signals = AnalyzeSignals(data.CashFlow);

            // Rule 1: Có dấu hiệu GIÁN TIẾP, không có TRỰC TIẾP
            if (signals.HasIndirect && !signals.HasDirect)
            {
                data.CashFlowMethod = "indirect";
                return;
            }

            // Rule 2: Có dấu hiệu TRỰC TIẾP, không có GIÁN TIẾP
            if (signals.HasDirect && !signals.HasIndirect)
            {
                data.CashFlowMethod = "direct";
                return;
            }

            // Rule 3: Có CẢ HAI → ưu tiên GIÁN TIẾP (VAS + thực tế trình bày)
            if (signals.HasDirect && signals.HasIndirect)
            {
                data.CashFlowMethod = "indirect";
                return;
            }

            // Rule 4: Không có dấu hiệu đủ mạnh → GIỮ NULL
            // Không được đoán
        }

        // =========================
        // SIGNAL ANALYSIS
        // =========================

        private static CashFlowSignals AnalyzeSignals(List<Row> cashFlowRows)
        {
            var items = cashFlowRows
                .Select(r => r.Item?.ToLowerInvariant() ?? string.Empty)
                .ToList();

            return new CashFlowSignals
            {
                HasIndirect = ContainsAny(items, IndirectKeywords),
                HasDirect = ContainsAny(items, DirectKeywords)
            };
        }

        private static bool ContainsAny(List<string> items, List<string> keywords)
        {
            foreach (var item in items)
            {
                foreach (var kw in keywords)
                {
                    if (item.Contains(kw))
                        return true;
                }
            }
            return false;
        }

        // =========================
        // KEYWORDS (VAS-BASED)
        // =========================

        private static readonly List<string> IndirectKeywords = new()
        {
            "lợi nhuận",
            "lãi",
            "khấu hao",
            "hao mòn",
            "dự phòng",
            "điều chỉnh",
            "chênh lệch",
            "vốn lưu động",
            "thay đổi vốn",
            "chi phí lãi vay",
            "thu nhập lãi",
            "lãi/(lỗ)"
        };

        private static readonly List<string> DirectKeywords = new()
        {
            "tiền thu",
            "tiền chi",
            "thu từ",
            "chi cho",
            "trả cho",
            "nộp thuế",
            "nộp ngân sách",
            "chi trả",
            "thu hồi"
        };
    }
}
