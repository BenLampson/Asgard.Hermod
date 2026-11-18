namespace Asgard.Hermod.Abstractions.Models;

/// <summary>按已保存的连接版本授权；范围字段的含义由稳定的实例类型决定。
/// 允许任意上传来源必须明确保存，缺少字段的旧授权继续受本地目录限制。</summary>
public sealed record McpGrant(string Id, string InstanceId, string TypeId, string Revision,
    string Resource, string Qualifier, string Table, bool ShareContent,
    string? TransferRoot = null, bool AllowUpload = false, bool AllowDownload = false,
    bool AllowCreateFolder = false, bool AllowDelete = false, bool AllowSignedLinks = false, bool AllowOverwrite = false,
    bool AllowUnrestrictedUpload = false);
