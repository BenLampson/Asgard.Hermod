namespace Asgard.Hermod.Abstractions.Models;

/// <summary>冻结一次 MCP 访问的实例、操作和位置；下载可要求用户选择本机目录。</summary>
public sealed record McpApprovalRequest(string InstanceName, string InstanceType, string Operation,
    IReadOnlyList<McpApprovalField> Fields, bool RequiresDownloadFolder = false,
    string SuggestedDownloadFolder = "", string DownloadFileName = "", bool ShowDownloadFolder = false);
