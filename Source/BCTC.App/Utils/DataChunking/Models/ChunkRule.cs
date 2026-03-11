using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils.DataChunking.Models
{
    public class ChunkRule
    {
        public int? Id { get; set; }
        public List<string> Texts { get; set; } // For AND conditions
        public bool RequireAllTexts { get; set; } // true for AND, false for OR
        public bool Inclusive { get; set; } // Include this item in result

        public ChunkRule()
        {
            Texts = new List<string>();
            RequireAllTexts = false;
            Inclusive = false;
        }
    }
}
