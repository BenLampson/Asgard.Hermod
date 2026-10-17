namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>限制 OSS 区域与容器的地址组成字符，凭据永不发送到用户提供的域名。</summary>
public static class AliyunOssEndpoint
{
    /// <summary>OSS 旧 Location 含 oss- 前缀；新 Region 可直接使用。</summary>
    public static string NormalizeRegion(string? region)
    {
        string value = region?.StartsWith("oss-", StringComparison.Ordinal) == true ? region[4..] : region ?? "";
        if (value.Length is < 2 or > 48 || value[0] is < 'a' or > 'z' || value.EndsWith('-') ||
            value.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-'))
            throw ObjectStorageException.FromResource("InvalidBucketRegionRefreshTheBucketList", "InvalidRegion");
        return value;
    }

    /// <summary>普通 OSS 容器名称只允许小写 ASCII、数字和连字符。</summary>
    public static void ValidateBucket(string bucket)
    {
        if (bucket is null || bucket.Length is < 3 or > 63 || bucket.StartsWith('-') || bucket.EndsWith('-') ||
            bucket.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-'))
            throw ObjectStorageException.FromResource("InvalidBucketNameSelectTheBucketAgain", "InvalidBucket");
    }
}
