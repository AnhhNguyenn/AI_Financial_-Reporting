namespace BCTC.DataAccess.Models
{
    public sealed class UsageInfo
    {
        public int ScanInputTokens { get; set; }
        public int ScanOutputTokens { get; set; }
        public int ScanTotalTokens => ScanInputTokens + ScanOutputTokens;

        public int MapInputTokens { get; set; }
        public int MapOutputTokens { get; set; }
        public int MapTotalTokens => MapInputTokens + MapOutputTokens;

        public int TotalTokens => ScanTotalTokens + MapTotalTokens;

        public static readonly UsageInfo Empty = new();
    }
}
