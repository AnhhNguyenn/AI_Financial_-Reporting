using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Settings
{
    public class MappingSettings
    {
        public int MaxRetries { get; set; }
        public int RetryDelayMilliseconds { get; set; }
        public int TimeoutSeconds { get; set; }
        public string ModelProvider { get; set; }
        public ModelConfig ModelOpenAI { get; set; }
        public ModelConfig ModelGoogle { get; set; }
    }
}
