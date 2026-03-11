using MappingReportNorm.Models;

namespace BCTC.DataAccess.Models
{
    public class FinancialReportModel
    {
        public int BusinessTypeID { get; set; }
        public string BusinessTypeName { get; set; }
        public string Company { get; set; }
        public string Currency { get; set; }
        public string? BaseCurrency { get; set; }
        public long? CurrencyUnit { get; set; }
        public List<FinancialReportItem> IncomeStatement { get; set; }
        public List<FinancialReportItem> BalanceSheet { get; set; }
        public List<FinancialReportItem> CashFlow { get; set; }
        public List<FinancialReportItem> OffBalanceSheet { get; set; }

        public string CashFlowMethod { get; set; }

        public Meta Meta { get; set; }
        public MetaDB MetaDB { get; set; }
    }
}
