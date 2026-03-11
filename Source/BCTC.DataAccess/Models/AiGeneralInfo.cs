namespace BCTC.DataAccess.Models
{
    public class AiGeneralInfo
    {
        public string? Currency { get; set; }
        public string? BaseCurrency { get; set; }
        public long? CurrencyUnit { get; set; }
        public string? CashFlowMethod { get; set; }
        public MetaDB? MetaDB { get; set; }
    }
}
