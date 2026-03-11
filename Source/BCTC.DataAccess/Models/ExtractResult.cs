namespace BCTC.DataAccess.Models
{
    public class ExtractResult
    {
        public int? BusinessTypeID { get; set; }
        public string? BusinessTypeName { get; set; }
        public string? Company { get; set; }
        public string? Currency { get; set; }
        public string? BaseCurrency { get; set; }
        public long? CurrencyUnit { get; set; }
        public List<Row> IncomeStatement { get; set; } = new();
        public List<Row> BalanceSheet { get; set; } = new();
        public List<Row> CashFlow { get; set; } = new();
        public List<Row> OffBalanceSheet { get; set; } = new();
        public string? CashFlowMethod { get; set; }
        public Meta? Meta { get; set; }
        public MetaDB? MetaDB { get; set; }
    }
}