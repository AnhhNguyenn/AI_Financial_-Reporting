using BCTC.App.Utils;
using BCTC.DataAccess.Models;
using BCTC.DataAccess.Repositories.Interfaces;
using MappingReportNorm.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace MappingReportNorm.Services
{
    public class FinancialService : IFinancialService
    {
        private readonly ILogger<FinancialService> _logger;
        private readonly IFinanceFullRepository _financeFullRepository;
        private readonly ICacheService _cache;
        public FinancialService(
            ILogger<FinancialService> logger,
            IFinanceFullRepository financeFullRepository,
            ICacheService cache
        )
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _financeFullRepository = financeFullRepository ?? throw new ArgumentNullException(nameof(financeFullRepository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        private IEnumerable<ReportNorm> GetAppendReportNorms()
        {
            var appendItems = new List<ReportNorm>
            {
                new ReportNorm
                {
                    ReportNormID = 1101,
                    Name = "Các quỹ bù trừ",
                    ParentReportNormID = 4510,
                    ReportComponentID = 46,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 64,
                    ReportComponentCode = "CK_CD",
                    Ordering = 42.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1102,
                    Name = "Doanh thu nghiệp vụ tư vấn đầu tư chứng khoán",
                    ParentReportNormID = 4603,
                    ReportComponentID = 47,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 64,
                    ReportComponentCode = "CK_KQ",
                    Ordering = 16m
                },
                new ReportNorm
                {
                    ReportNormID = 1103,
                    Name = "Doanh thu hoạt động tư vấn tài chính",
                    ParentReportNormID = 4603,
                    ReportComponentID = 47,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 64,
                    ReportComponentCode = "CK_KQ",
                    Ordering = 16m
                },
                new ReportNorm
                {
                    ReportNormID = 1104,
                    Name = "Chi phí tư nghiệp vụ tư vẫn đầu tư chứng khoán",
                    ParentReportNormID = 5447,
                    ReportComponentID = 47,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 64,
                    ReportComponentCode = "CK_KQ",
                    Ordering = 39m
                },
                new ReportNorm
                {
                    ReportNormID = 1105,
                    Name = "Chi phí hoạt động tư vấn tài chính",
                    ParentReportNormID = 5447,
                    ReportComponentID = 47,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 64,
                    ReportComponentCode = "CK_KQ",
                    Ordering = 39m
                },
                new ReportNorm
                {
                    ReportNormID = 1106,
                    Name = "Chi phí thuế bổ sung cho các kỳ trước theo quyết định của cơ quan thuế",
                    ParentReportNormID = 5479,
                    ReportComponentID = 47,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 64,
                    ReportComponentCode = "CK_KQ",
                    Ordering = 72.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1107,
                    Name = "Chi phí dự phòng các khoản cho vay và phải thu",
                    ParentReportNormID = 4591,
                    ReportComponentID = 47,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 64,
                    ReportComponentCode = "CK_KQ",
                    Ordering = 45.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1108,
                    Name = "Vốn góp liên doanh",
                    ParentReportNormID = 4317,
                    ReportComponentID = 29,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_CD_NHNN",
                    Ordering = 24.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1109,
                    Name = "Đầu tư vào công ty liên kết",
                    ParentReportNormID = 4317,
                    ReportComponentID = 29,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_CD_NHNN",
                    Ordering = 24.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1110,
                    Name = "Cam kết giao dịch hối đoái",
                    ParentReportNormID = 3975,
                    ReportComponentID = 32,
                    ReportComponentTypeID = 27,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_NB_NHNN",
                    Ordering = 8m
                },
                new ReportNorm
                {
                    ReportNormID = 1111,
                    Name = "Các khoản xử lý theo đề án tái cấu trúc ngân hàng",
                    ParentReportNormID = 4392,
                    ReportComponentID = 30,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_KQ_NHNN",
                    Ordering = 16.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1112,
                    Name = "Doanh thu khác hoạt động kinh doanh bảo hiểm",
                    ParentReportNormID = 3212,
                    ReportComponentID = 11,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_KQ",
                    Ordering = 19.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1113,
                    Name = "Tiền thu từ hoạt động kinh doanh nhận, nhượng tái bảo hiểm",
                    ParentReportNormID = 3693,
                    ReportComponentID = 13,
                    ReportComponentTypeID = 20,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCTT",
                    Ordering = 2.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1114,
                    Name = "Tiền chi cho hoạt động kinh doanh nhận, nhượng tái bảo hiểm",
                    ParentReportNormID = 3693,
                    ReportComponentID = 13,
                    ReportComponentTypeID = 20,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCTT",
                    Ordering = 8.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1115,
                    Name = "Nhận ký quỹ dài hạn",
                    ParentReportNormID = 3116,
                    ReportComponentID = 18,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_CD",
                    Ordering = 108.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1116,
                    Name = "Tiền thu từ bán hàng, cung cấp dịch vụ và doanh thu khác",
                    ParentReportNormID = 3693,
                    ReportComponentID = 13,
                    ReportComponentTypeID = 20,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCTT",
                    Ordering = 5.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1117,
                    Name = "Tiền chi cho vay, mua các công cụ nợ của đơn vị khác",
                    ParentReportNormID = 3687,
                    ReportComponentID = 13,
                    ReportComponentTypeID = 20,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCTT",
                    Ordering = 20.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1118,
                    Name = "Tiền thu hồi cho vay, bán lại các công cụ nợ của đơn vị khác",
                    ParentReportNormID = 3687,
                    ReportComponentID = 13,
                    ReportComponentTypeID = 20,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCTT",
                    Ordering = 17.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1119,
                    Name = "Cam kết cho vay không hủy ngang",
                    ParentReportNormID = 3975,
                    ReportComponentID = 32,
                    ReportComponentTypeID = 27,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_NB_NHNN",
                    Ordering = 8.9m
                },
                new ReportNorm
                {
                    ReportNormID = 1120,
                    Name = "Tiền thu từ hoạt động repo",
                    ParentReportNormID = 3688,
                    ReportComponentID = 13,
                    ReportComponentTypeID = 20,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCTT",
                    Ordering = 28.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1121,
                    Name = "Tiền chi trả hoạt động repo",
                    ParentReportNormID = 3648,
                    ReportComponentID = 12,
                    ReportComponentTypeID = 15,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCGT",
                    Ordering = 35.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1122,
                    Name = "Tiền chi trả hoạt động repo",
                    ParentReportNormID = 3688,
                    ReportComponentID = 13,
                    ReportComponentTypeID = 20,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCTT",
                    Ordering = 30.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1123,
                    Name = "Lợi nhuận năm nay",
                    ParentReportNormID = 4343,
                    ReportComponentID = 29,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_CD_NHNN",
                    Ordering = 74.1m
                },
                new ReportNorm
                {
                    ReportNormID = 1124,
                    Name = "Lợi nhuận lũy kế đến cuối năm trước",
                    ParentReportNormID = 4343,
                    ReportComponentID = 29,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_CD_NHNN",
                    Ordering = 74.2m
                },
                new ReportNorm
                {
                    ReportNormID = 1125,
                    Name = "Cam kết mua ngoại tệ",
                    ParentReportNormID = 1110,
                    ReportComponentID = 32,
                    ReportComponentTypeID = 27,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_NB_NHNN",
                    Ordering = 8.1m
                },
                new ReportNorm
                {
                    ReportNormID = 1126,
                    Name = "Cam kết bán ngoại tệ",
                    ParentReportNormID = 1110,
                    ReportComponentID = 32,
                    ReportComponentTypeID = 27,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_NB_NHNN",
                    Ordering = 8.2m
                },
                new ReportNorm
                {
                    ReportNormID = 1127,
                    Name = "Cam kết mua - giao dịch hoán đối tiên tệ",
                    ParentReportNormID = 1110,
                    ReportComponentID = 32,
                    ReportComponentTypeID = 27,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_NB_NHNN",
                    Ordering = 8.3m
                },
                new ReportNorm
                {
                    ReportNormID = 1128,
                    Name = "Cam kết bán - giao dịch hoán đôi tiên tệ",
                    ParentReportNormID = 1110,
                    ReportComponentID = 32,
                    ReportComponentTypeID = 27,
                    ReportTemplateID = 58,
                    ReportComponentCode = "NH_NB_NHNN",
                    Ordering = 8.4m
                },
                //new ReportNorm
                //{
                //    ReportNormID = 1129,
                //    Name = "Trả tiền lãi vay",
                //    ParentReportNormID = 3695,
                //    ReportComponentID = 13,
                //    ReportComponentTypeID = 20,
                //    ReportTemplateID = 57,
                //    ReportComponentCode = "BH_LCTT",
                //    Ordering = 14.5m
                //},
                //new ReportNorm
                //{
                //    ReportNormID = 1130,
                //    Name = "Tiền lãi vay đã trả",
                //    ParentReportNormID = 3717,
                //    ReportComponentID = 13,
                //    ReportComponentTypeID = 20,
                //    ReportTemplateID = 57,
                //    ReportComponentCode = "BH_LCTT",
                //    Ordering = 11.5m
                //},
                new ReportNorm
                {
                    ReportNormID = 1131,
                    Name = "Tiền",
                    ParentReportNormID = 3717,
                    ReportComponentID = 13,
                    ReportComponentTypeID = 20,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_LCTT",
                    Ordering = 11.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1132,
                    Name = "Chi phí sản xuất, kinh doanh dở dang dài hạn",
                    ParentReportNormID = 5360,
                    ReportComponentID = 18,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_CD",
                    Ordering = 60.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1133,
                    Name = "Phải trả về hợp đồng tài chính",
                    ParentReportNormID = 5325,
                    ReportComponentID = 18,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_CD",
                    Ordering = 84.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1134,
                    Name = "Dự phòng lãi cam kết đầu tư tối thiếu",
                    ParentReportNormID = 3117,
                    ReportComponentID = 18,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_CD",
                    Ordering = 101.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1135,
                    Name = "Dự phòng giảm giá đầu tư ngắn hạn",
                    ParentReportNormID = 3103,
                    ReportComponentID = 18,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_CD",
                    Ordering = 7.2m
                },
                new ReportNorm
                {
                    ReportNormID = 1136,
                    Name = "Đầu tư ngắn hạn khác",
                    ParentReportNormID = 3103,
                    ReportComponentID = 18,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_CD",
                    Ordering = 8.5m
                },
                new ReportNorm
                {
                    ReportNormID = 1137,
                    Name = "Đầu tư ngắn hạn",
                    ParentReportNormID = 3103,
                    ReportComponentID = 18,
                    ReportComponentTypeID = 14,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_CD",
                    Ordering = 7.1m
                },
                new ReportNorm
                {
                    ReportNormID = 1138,
                    Name = "Tăng dự phòng nghiệp vụ bảo hiểm gốc",
                    ParentReportNormID = 5340,
                    ReportComponentID = 11,
                    ReportComponentTypeID = 12,
                    ReportTemplateID = 57,
                    ReportComponentCode = "BH_KQ",
                    Ordering = 30.5m
                },
            };

            return appendItems;
        }

        public async Task<IEnumerable<ReportNorm>> GetAllReportNormsAsync()
        {
            const string keyCache = "Financial:AllReportNorms";

            var cached = await _cache.GetAsync<List<ReportNorm>>(keyCache);
            if (cached != null)
                return cached;

            var results = await _financeFullRepository.GetAllReportNormsAsync();

            var finalResults = results?.ToList() ?? new List<ReportNorm>();

            var appendItems = GetAppendReportNorms();
            finalResults.AddRange(appendItems);

            if (finalResults.Any())
            {
                await _cache.SetAsync(keyCache, finalResults, TimeSpan.FromHours(1));
            }

            return finalResults;
        }
        public async Task<IEnumerable<ReportNorm>> GetAllReportNormsDB()
        {
            const string keyCache = "Financial:AllReportNormsBD";

            var cached = await _cache.GetAsync<List<ReportNorm>>(keyCache);
            if (cached != null)
                return cached;

            var results = await _financeFullRepository.GetAllReportNormsAsync() ?? new List<ReportNorm>();

            return results;
        }
        public async Task<(ReportDataItem, IEnumerable<ReportDataDetailItem>)> GetReportData(string stockCode, string reportTermCode, int year,
            string unitedCode, string adjustedCode, string auditedStatusCode)
        {
            int reportTermId = Util.GetReportTermId(reportTermCode);
            int unitedId = Util.GetUnitedID(unitedCode);
            int adjustedId = Util.GetAdjustedID(adjustedCode);
            int auditedStatusId = Util.GetAuditedStatusID(auditedStatusCode);

            var results = await _financeFullRepository.GetReportData(stockCode, reportTermId, year, unitedId, adjustedId, auditedStatusId);
            return results;
        }
        public async Task<IEnumerable<ReportDataDetailItem>> GetReportDataFull(string stockCode, string reportTermCode, int year, string unitedCode, string auditedStatusCode, int isQK)
        {
            int reportTermId = Util.GetReportTermId(reportTermCode);
            int unitedId = Util.GetUnitedID(unitedCode);

            var results = await _financeFullRepository.GetReportDataFull(stockCode, reportTermId, year, unitedId, auditedStatusCode, isQK);
            return results;
        }
    }
}
