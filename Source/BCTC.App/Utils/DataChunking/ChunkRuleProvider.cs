using BCTC.DataAccess.Models.Enum;
using MappingReportNorm.Utils.DataChunking.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MappingReportNorm.Utils.DataChunking
{
    public static class ChunkRuleProvider
    {
        private static readonly Dictionary<ReportTemplate, ChunkConfiguration> _configurations;

        static ChunkRuleProvider()
        {
            _configurations = new Dictionary<ReportTemplate, ChunkConfiguration>
            {
                // Công ty cổ phần
                {
                    ReportTemplate.JointStock,
                    new ChunkConfiguration
                    {
                        Template = ReportTemplate.JointStock,
                        Steps = new List<ChunkStep>
                        {
                            // Start -> 3001["TÀI SẢN DÀI HẠN"]
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 3001,
                                        Texts = new List<string> { "TÀI SẢN DÀI HẠN" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            // 3001 -> 2994["NGUỒN VỐN"] | 2997["NỢ PHẢI TRẢ"]
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 2994,
                                        Texts = new List<string> { "NGUỒN VỐN" },
                                        Inclusive = false
                                    },
                                    new ChunkRule
                                    {
                                        Id = 2997,
                                        Texts = new List<string> { "NỢ PHẢI TRẢ" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            // 2994/2997 -> 2998["VỐN CHỦ SỞ HỮU"]
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 2998,
                                        Texts = new List<string> { "VỐN CHỦ SỞ HỮU" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            // 2998 -> End (take all remaining)
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>(),
                                IncludeEndItem = true
                            }
                        }
                    }
                },
                
                // Ngân hàng
                {
                    ReportTemplate.Bank,
                    new ChunkConfiguration
                    {
                        Template = ReportTemplate.Bank,
                        Steps = new List<ChunkStep>
                        {
                            // Start -> 4303["NỢ PHẢI TRẢ" && "VỐN CHỦ SỞ HỮU"]
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 4303,
                                        Texts = new List<string> { "NỢ PHẢI TRẢ", "VỐN CHỦ SỞ HỮU" },
                                        RequireAllTexts = true,
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            // 4303 -> End
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>(),
                                IncludeEndItem = true
                            }
                        }
                    }
                },

                // Chứng khoán
                {
                    ReportTemplate.Securities,
                    new ChunkConfiguration
                    {
                        Template = ReportTemplate.Securities,
                        Steps = new List<ChunkStep>
                        {
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 4481,
                                        Texts = new List<string> { "TÀI SẢN DÀI HẠN" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 4474,
                                        Texts = new List<string> { "NGUỒN VỐN" },
                                        Inclusive = false
                                    },
                                    new ChunkRule
                                    {
                                        Id = 4477,
                                        Texts = new List<string> { "NỢ PHẢI TRẢ" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 4478,
                                        Texts = new List<string> { "VỐN CHỦ SỞ HỮU" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>(),
                                IncludeEndItem = true
                            }
                        }
                    }
                },

                // Bảo hiểm
                {
                    ReportTemplate.Insurance,
                    new ChunkConfiguration
                    {
                        Template = ReportTemplate.Insurance,
                        Steps = new List<ChunkStep>
                        {
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 3100,
                                        Texts = new List<string> { "TÀI SẢN DÀI HẠN" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 3093,
                                        Texts = new List<string> { "NGUỒN VỐN" },
                                        Inclusive = false
                                    },
                                    new ChunkRule
                                    {
                                        Id = 3096,
                                        Texts = new List<string> { "NỢ PHẢI TRẢ" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>
                                {
                                    new ChunkRule
                                    {
                                        Id = 3097,
                                        Texts = new List<string> { "VỐN CHỦ SỞ HỮU" },
                                        Inclusive = false
                                    }
                                },
                                IncludeEndItem = false
                            },
                            new ChunkStep
                            {
                                Rules = new List<ChunkRule>(),
                                IncludeEndItem = true
                            }
                        }
                    }
                }
            };
        }

        public static ChunkConfiguration GetConfiguration(ReportTemplate template)
        {
            if (_configurations.TryGetValue(template, out var config))
            {
                return config;
            }
            throw new ArgumentException($"Configuration not found for template: {template}");
        }
    }
}
