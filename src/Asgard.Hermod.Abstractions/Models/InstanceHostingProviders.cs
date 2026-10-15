namespace Asgard.Hermod.Abstractions.Models;

/// <summary>SSH 与数据库共用托管品牌目录；品牌不参与连接或认证逻辑。</summary>
public static class InstanceHostingProviders
{
    /// <summary>云端沿用加密参数信封携带展示元数据，兼容现有同步服务。</summary>
    public const string MetadataKey = "$hermod.hostingProvider";

    /// <summary>在语言初始化后解析展示名称，空标识表示自部署。</summary>
    public static IReadOnlyList<InstanceHostingProvider> All =>
    [
        new("", Text.Get("HostingSelfHosted"), "monitor"),
        new("aliyun", Text.Get("HostingAlibabaCloud"), "provider-aliyun-oss"),
        new("tencent", Text.Get("HostingTencentCloud"), "provider-tencent-cos"),
        new("volcengine", Text.Get("HostingVolcengine"), "provider-volcengine-tos"),
        new("aws", Text.Get("HostingAws"), "provider-aws-s3"),
    ];

    /// <summary>这些类型的连接器表示连接方式或拓扑，托管品牌使用独立元数据。</summary>
    public static bool Supports(string typeId) => typeId is "ssh" or "mysql" or "redis";

    /// <summary>保存只接受已注册的稳定品牌标识，不接受翻译后的名称。</summary>
    public static bool IsValid(string? id) => id is "" or "aliyun" or "tencent" or "volcengine" or "aws";

    /// <summary>未知旧值提供安全的默认展示，不把资源键或协议标识暴露给用户。</summary>
    public static InstanceHostingProvider Get(string id) => All.FirstOrDefault(item => item.Id == id) ?? All[0];
}
