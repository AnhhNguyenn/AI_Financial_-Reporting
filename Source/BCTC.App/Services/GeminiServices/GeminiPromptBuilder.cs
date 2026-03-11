using BCTC.DataAccess.Models.Norm;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BCTC.App.Services.GeminiServices
{
    public partial class GeminiService
    {
        public static class GeminiPromptBuilder
        {
            public static string BuildSystemInstruction()
            {
                return @"
ROLE
Extract financial tables from PDF/images into structured JSON.

RULES
- Extract only visible data.
- Do NOT interpret financial meaning.
- Do NOT infer missing values.

--------------------------------------------------

OUTPUT FORMAT

Return a JSON Array.

Each visual row = one object.

[
{
""Code"": ""string|null"",
""Item"": ""string"",
""ParentName"": ""string|null"",
""Values"": {
""KyNay"": number|null,
""KyTruoc"": number|null,
""LuyKe_KyNay"": number|null,
""LuyKe_KyTruoc"": number|null
}
}
]

--------------------------------------------------

COLUMN MAPPING

Use header text only.

""Năm nay"" or larger year → KyNay
""Năm trước"" or smaller year → KyTruoc
""Lũy kế"" + larger year → LuyKe_KyNay
""Lũy kế"" + smaller year → LuyKe_KyTruoc
""Thuyết minh"" → ignore

--------------------------------------------------

ROW EXTRACTION (CRITICAL)

Process table TOP → DOWN.

For each row:
1. Detect item text (left column).
2. Detect numbers on the SAME horizontal line.
3. Assign numbers to that row.

Rules:
- Never take numbers from next row.
- Never move numbers upward.
- If no numbers exist → null.

--------------------------------------------------

ROW PRESERVATION

Rows containing ""-"" or blank cells still exist.

""-"" = null
Do not merge rows.

--------------------------------------------------

ITEM TEXT

Join wrapped lines into one item name.

--------------------------------------------------

HIERARCHY

Detect parent using:
- indentation
- numbering (A, I, 1, 1.1)
- bold / uppercase

ParentName = nearest parent ABOVE.
Top level → null.

--------------------------------------------------

VALUE CLEANING

Remove thousand separators.

(1.000) → -1000
-1.000 → -1000
""-"" → null

--------------------------------------------------

OUTPUT

Return JSON Array only.
No markdown.
No explanations.
";
            }

            public static string BuildRouterPrompt()
            {
                return @"
TASK
Find page ranges for financial tables.

Tables:
BS  = Balance Sheet
PL  = Income Statement
CF  = Cash Flow
OBS = Off-Balance Sheet

--------------------------------------------------

ALGORITHM (BIDIRECTIONAL SCAN)

Step 1 — Locate core pages
Find page with table title → T_Start
Find page with totals or signatures → T_End

Step 2 — Scan upward
Check page (T_Start - 1)

If it contains beginning of the same table
→ move Start_Page upward.

If it is another table
→ keep T_Start.

Step 3 — Scan downward
Check page (T_End + 1)

If it contains continuation or signatures
→ extend End_Page.

Stop when a new table title appears.

--------------------------------------------------

TABLE RULES

BS (Balance Sheet)

After ""TỔNG TÀI SẢN"" keep scanning down.

If next pages contain:
""NỢ PHẢI TRẢ""
""VỐN CHỦ SỞ HỮU""

→ include them.

Stop when seeing:
""NGOẠI BẢNG""
""KẾT QUẢ KINH DOANH""

--------------------------------------------------

OBS (Off-Balance Sheet)

Often appears after BS.

Check:
- title at bottom of BS page
- continuation before PL

--------------------------------------------------

PL (Income Statement)

After ""Lợi nhuận sau thuế"" keep scanning.

Include rows like:

""Lãi cơ bản trên cổ phiếu""
""Tổng thu nhập toàn diện""

Pages containing only signatures still belong to PL.

--------------------------------------------------

CF (Cash Flow)

Often longest table.

After main sections continue scanning.

Include rows like:

""Lưu chuyển tiền từ hoạt động môi giới""
""Tiền gửi của khách hàng""

--------------------------------------------------

OUTPUT

Return JSON only:

{
""tables"": [
{ ""type"": ""BS"", ""start_page"": number, ""end_page"": number },
{ ""type"": ""PL"", ""start_page"": number, ""end_page"": number },
{ ""type"": ""CF"", ""start_page"": number, ""end_page"": number },
{ ""type"": ""OBS"", ""start_page"": number, ""end_page"": number }
]
}
";
            }

            public static string BuildGeneralInfoPrompt()
            {
                return @"
TASK
Extract general information from the financial report.

OUTPUT

{
""Company"": ""string|null"",
""Currency"": ""string|null"",
""BaseCurrency"": ""string|null"",
""CurrencyUnit"": number|null,
""CashFlowMethod"": ""Direct|Indirect|null"",
""MetaDB"": {
""AuditedNote"": ""string|null"",
""CtyKiemToan"": ""string|null"",
""NgayKiemToan"": ""string|null""
}
}

RULES

CurrencyUnit:
""triệu đồng"" → 1000000
""nghìn/ngàn đồng"" → 1000
not specified → 1

CashFlowMethod:

Indirect:
""phương pháp gián tiếp""
or
starts with ""Lợi nhuận trước thuế"" + adjustment rows

Direct:
""phương pháp trực tiếp""
or
rows like ""Tiền thu từ..."" ""Tiền chi cho...""

If uncertain → null.

Return JSON only.
";
            }

            public static string BuildBalanceSheetPrompt(int start, int end)
            {
                return $@"
TASK
Extract Balance Sheet.

Scope: pages {start} → {end}

Include:
Assets
Liabilities
Equity

Rules
- Use column mapping from System Instruction.
- Bank / securities reports may have special items.
- Do not skip rows.
- Values must contain all four keys.

Return JSON Array only.
";
            }

            public static string BuildIncomeStatementPrompt(int start, int end)
            {
                return $@"
TASK
Extract Income Statement.

Scope: pages {start} → {end}

Include:
Revenue
Expenses
Profit
EPS if present.

Rules
Quarterly reports often contain ""Lũy kế"" columns.

Map accordingly:
KyNay / KyTruoc
LuyKe_KyNay / LuyKe_KyTruoc

Return JSON Array only.
";
            }

            public static string BuildCashFlowPrompt(int start, int end)
            {
                return $@"
TASK
Extract Cash Flow Statement.

Scope: pages {start} → {end}

Include all cash flows:

Operating
Investing
Financing

Securities companies may include:
brokerage flows
customer deposits

Return JSON Array only.
";
            }

            public static string BuildOffBalanceSheetPrompt(int start, int end)
            {
                return $@"
TASK
Extract Off-Balance Sheet items.

Scope: pages {start} → {end}

Common items include:

Foreign currencies
Credit limits
Guarantee commitments
Custodian assets
Leased assets

Return JSON Array only.
";
            }

            public static string BuildNormPrompt(
                List<RowForPrompt> rows,
                List<NormRow> norms,
                int businessTypeId)
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                };

                var rowsWithId = rows.Select((r, idx) => new
                {
                    id = idx,
                    c = r.Code,
                    i = r.Item,
                    p = r.ParentName
                });

                var normsOptimized = norms.Select(n => new
                {
                    id = n.ReportNormID,
                    c = n.PublishNormCode,
                    i = n.Name,
                    p = n.ParentName
                });

                var sample = new
                {
                    results = new[] { new { id = 0, norm = "FOUND_ID_OR_NULL" } }
                };

                var sb = new StringBuilder();
                sb.AppendLine("ROLE: Data Matching Expert");
                sb.AppendLine("TASK: Map ROW → NORM bằng 3 yếu tố: Code + Item + Parent");
                sb.AppendLine("QUY TẮC:");
                sb.AppendLine("- Code khớp nhưng Item/Parent sai bản chất → TỪ CHỐI");
                sb.AppendLine("- Không có Code → so khớp Item + Parent");
                sb.AppendLine("- Parent là yếu tố khóa ngữ cảnh");

                sb.AppendLine("OUTPUT JSON:");
                sb.AppendLine(JsonSerializer.Serialize(sample, jsonOptions));
                sb.AppendLine("NORMS:");
                sb.AppendLine(JsonSerializer.Serialize(normsOptimized, jsonOptions));
                sb.AppendLine("ROWS:");
                sb.AppendLine(JsonSerializer.Serialize(rowsWithId, jsonOptions));

                return sb.ToString();
            }
        }
    }
}