using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BCTC.App.Services.MappingCache
{
    /// <summary>
    /// Cache item đã được validate - chỉ lưu kết quả ĐÚNG
    /// </summary>
    public class ValidatedMappingCache
    {
        // === IDENTIFICATION ===
        [JsonPropertyName("stockCode")]
        public string StockCode { get; set; }

        [JsonPropertyName("businessTypeId")]
        public int BusinessTypeID { get; set; }

        [JsonPropertyName("componentType")]
        public int ComponentType { get; set; }  // 14=BS, 12=IS, 15=CF, 20=CF_Direct...

        // === SOURCE DATA (từ PDF scan) ===
        [JsonPropertyName("itemName")]
        public string ItemName { get; set; }

        [JsonPropertyName("parentName")]
        public string ParentName { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }  // Có thể null

        // === MAPPING RESULT ===
        [JsonPropertyName("reportNormId")]
        public int ReportNormID { get; set; }

        [JsonPropertyName("normName")]
        public string NormName { get; set; }

        [JsonPropertyName("publishNormCode")]
        public string PublishNormCode { get; set; }

        // === VALIDATION INFO ===
        [JsonPropertyName("status")]
        public ValidationStatus Status { get; set; }

        [JsonPropertyName("validationMethod")]
        public string ValidationMethod { get; set; }  // "AI", "Code", "Manual"

        [JsonPropertyName("confidenceScore")]
        public int ConfidenceScore { get; set; }  // 0-100

        [JsonPropertyName("comparisonValue")]
        public decimal? ComparisonValue { get; set; }  // Giá trị từ scan

        [JsonPropertyName("dbValue")]
        public decimal? DbValue { get; set; }  // Giá trị trong DB

        [JsonPropertyName("errorMarginPct")]
        public decimal? ErrorMarginPct { get; set; }  // Sai số %

        // === METADATA ===
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("lastUsedAt")]
        public DateTime LastUsedAt { get; set; }

        [JsonPropertyName("lastValidatedAt")]
        public DateTime? LastValidatedAt { get; set; }

        [JsonPropertyName("usageCount")]
        public int UsageCount { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }

        // === AUDIT ===
        [JsonPropertyName("notes")]
        public string Notes { get; set; }
    }

    /// <summary>
    /// Container chứa tất cả cache của 1 công ty
    /// </summary>
    public class CompanyCacheContainer
    {
        [JsonPropertyName("stockCode")]
        public string StockCode { get; set; }

        [JsonPropertyName("businessTypeId")]
        public int BusinessTypeID { get; set; }

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonPropertyName("totalItems")]
        public int TotalItems { get; set; }

        [JsonPropertyName("items")]
        public List<ValidatedMappingCache> Items { get; set; } = new List<ValidatedMappingCache>();
    }
}