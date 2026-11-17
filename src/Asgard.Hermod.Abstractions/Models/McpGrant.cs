namespace Asgard.Hermod.Abstractions.Models;

/// <summary>按已保存的连接版本授权；范围字段的含义由稳定的实例类型决定。</summary>
public sealed record McpGrant(string Id, string InstanceId, string TypeId, string Revision,
    string Resource, string Qualifier, string Table, bool ShareContent,
    string? TransferRoot = null, bool AllowUpload = false, bool AllowDownload = false);
