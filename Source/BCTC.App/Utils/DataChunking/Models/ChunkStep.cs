using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils.DataChunking.Models
{
    public class ChunkStep
    {
        public List<ChunkRule> Rules { get; set; } // OR conditions between rules
        public bool IncludeEndItem { get; set; }

        public ChunkStep()
        {
            Rules = new List<ChunkRule>();
            IncludeEndItem = false;
        }
    }
}
