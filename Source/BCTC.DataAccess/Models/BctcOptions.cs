namespace BCTC.DataAccess.Models
{
    public sealed class BctcOptions
    {
        public List<string> ScanKeys { get; set; } = new();
        public List<string> MapKeys { get; set; } = new();
        public string Model { get; set; }
        public string ConnectionString { get; set; } = string.Empty;
        public int DefaultYear { get; set; }
        public Dictionary<string, int> ProcessingPriority { get; set; } = new Dictionary<string, int>();
        public WorkerOptions WorkerConfig { get; set; } = new WorkerOptions();
    }

    public class WorkerOptions
    {
        public int MaxConcurrent { get; set; }
        public int ScanWorkers { get; set; }
        public int MapWorkers { get; set; }
        public int ImportWorkers { get; set; }
        public int ClipPages { get; set; }
        public int MaxRetryAttempts { get; set; }
        public List<BusinessRuleConfig> BusinessRules { get; set; } = new();
    }
    public class BusinessRuleConfig
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public int Id { get; set; }
        public bool Enabled { get; set; }
    }
}