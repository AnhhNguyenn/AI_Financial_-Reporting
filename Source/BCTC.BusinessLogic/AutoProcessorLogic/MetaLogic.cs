using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Report;

namespace BCTC.BusinessLogic.AutoProcessorLogic
{
    public static class MetaLogic
    {
        public static void MergeMeta(ExtractResult data, CompanyReportDto db)
        {
            if (data == null || db == null) return;

            data.Meta ??= new Meta();

            data.Meta.MaCongTy = db.StockCode;
            data.Company = db.StockCode;
            data.Meta.TenCongTy = db.CompanyName;
            data.Meta.Nam = db.Year;
            data.Meta.KyBaoCao = db.ReportTerm;
            data.Meta.LoaiBaoCao = db.AbstractType;
            data.Meta.TrangThaiKiemDuyet = db.AuditedStatus;
            data.Meta.TinhChatBaoCao = db.UnitedName;
            data.Meta.ThuocTinhKhac = db.IsAdjusted == 1 ? "DC" : "CDC";
            data.Meta.NgayCongBoBCTC = db.Date;
            data.BusinessTypeID = db.BusinessTypeID;
            data.BusinessTypeName = db.BusinessTypeName;
            if (string.IsNullOrWhiteSpace(data.Currency))
                data.Currency = "VND";
            data.Meta.Url= db.Url;
        }
    }
}
