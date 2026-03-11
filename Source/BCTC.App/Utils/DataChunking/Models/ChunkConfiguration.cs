using BCTC.DataAccess.Models.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils.DataChunking.Models
{
    public class ChunkConfiguration
    {
        public ReportTemplate Template { get; set; }
        public List<ChunkStep> Steps { get; set; }

        public ChunkConfiguration()
        {
            Steps = new List<ChunkStep>();
        }
    }
}
