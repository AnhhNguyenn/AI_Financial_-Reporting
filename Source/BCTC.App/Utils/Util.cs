using BCTC.DataAccess.Models;
using BCTC.DataAccess.Models.Enum;
using MappingReportNorm.Models;
using MappingReportNorm.Services;
using MappingReportNorm.Utils.ScanDataParser.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BCTC.App.Utils
{
    public static class Util
    {
        public static ReportTemplate GetReportTemplateByBusinessTypeID(int businessTypeID)
        {
            switch (businessTypeID)
            {
                case 1:
                    return ReportTemplate.JointStock;
                case 2:
                    return ReportTemplate.Securities;
                case 3:
                    return ReportTemplate.Bank;
                case 5:
                    return ReportTemplate.Insurance;
                default:
                    throw new NotImplementedException();
            }
        }

        public static void SetScanIndex<T>(IEnumerable<T> list) where T : FinancialReportItem
        {
            if(list == null || list.Count() == 0)
                return;
            int index = 1;
            foreach (var item in list)
                item.ScanIndex = index++;
        }

        public static List<ScanItem> CreateScanItems(List<FinancialReportItem> items)
        {
            if (items == null || items.Count == 0)
                return new List<ScanItem>();

            return items.Select((item, index) => new ScanItem
            {
                Index = item.ScanIndex,
                Text = item.Item,
                ReportNormID = item.ReportNormID,
                ParentText = item.ParentName
            }).ToList();
        }


        public static (List<ScannedIndicator>, List<DatabaseIndicator>) PrepareIndicators(
            List<TreeNode> normalizedNodes,
            List<ReportNorm> reportNorms)
        {
            var scannedIndicators = normalizedNodes.Select(item => new ScannedIndicator
            {
                ScanIndex = item.Index,
                Name = item.Text,
                //FullPathName = item.FullPathText,
                ParentName = item.ParentText,
                ReportNormID = item.ReportNormID is > 0 ? item.ReportNormID : null
            }).ToList();

            var databaseIndicators = reportNorms.Select(item => new DatabaseIndicator
            {
                ReportNormID = item.ReportNormID,
                StandardName = item.Name,
                //FullPathName = item.FullPathName,
                ParentName = item.ParentName
            }).ToList();

            return (scannedIndicators, databaseIndicators);
        }

        public static int GetAdjustedID(string code) => code switch
        {
            "DC" => 1,
            "CDC" => 0,
            _ => -1
        };

        public static int GetUnitedID(string code) => code switch
        {
            "DL" => 1,
            "CTM" => 2,
            "HN" => 0,
            _ => -1
        };

        public static int GetAbstractedID(string code) => code switch
        {
            "TT" => 1,
            "CT" => 0,
            _ => -1
        };

        public static int GetAuditedStatusID(string code) => code switch
        {
            "SX" => 3,
            "CKT" => 4,
            "VSTTT" => 5,
            "VSTD" => 6,
            "KT" => 10,
            _ => -1
        };

        public static int GetReportTermId(string termCode) => termCode switch
        {
            "N" => 1,
            "Q1" => 2,
            "Q2" => 3,
            "Q3" => 4,
            "Q4" => 5,
            "2D" => 6,
            "4D" => 7,
            "5D" => 8,
            "6D" => 9,
            "7D" => 10,
            "8D" => 11,
            "9D" => 12,
            "10D" => 13,
            "11D" => 14,
            "2C" => 15,
            "4C" => 16,
            "5C" => 17,
            "6C" => 18,
            "7C" => 19,
            "8C" => 20,
            "9C" => 21,
            "10C" => 22,
            "11C" => 23,
            "Th1" => 24,
            "Th2" => 25,
            "Th3" => 26,
            "Th4" => 27,
            "Th5" => 28,
            "Th6" => 29,
            "Th7" => 30,
            "Th8" => 31,
            "Th9" => 32,
            "Th10" => 33,
            "Th11" => 34,
            "Th12" => 35,
            "N1" => 36,
            "W" => 37,
            _ => -1 
        };
    }
}