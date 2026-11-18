namespace Asgard.Hermod.Abstractions.Models;

/// <summary>用户对当前访问的决定和可选下载位置，不代表持久授权。</summary>
public sealed record McpApprovalResult(bool Approved, string DownloadFolder = "");
