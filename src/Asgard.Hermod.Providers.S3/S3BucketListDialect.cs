namespace Asgard.Hermod.Providers.S3;

/// <summary>声明兼容服务列桶 XML 字段和分页参数差异，不改变 SDK 签名或公共契约。</summary>
public sealed record S3BucketListDialect(string RegionElement, string CursorElement = "ContinuationToken",
    string CursorParameter = "continuation-token", string PageSizeParameter = "max-buckets");
