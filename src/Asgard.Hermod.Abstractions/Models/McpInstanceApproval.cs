namespace Asgard.Hermod.Abstractions.Models;

/// <summary>桌面用户明确选择始终允许的连接版本，以及未指定下载路径时使用的本机目录。</summary>
public sealed record McpInstanceApproval(string InstanceId, string TypeId, string Revision, string DownloadFolder = "")
{
    /// <summary>名称和标签可变，修改连接或重建实例后需要重新授权。</summary>
    public bool Matches(McpTarget target) => InstanceId == target.Id && TypeId == target.TypeId && Revision == target.Revision;
}
