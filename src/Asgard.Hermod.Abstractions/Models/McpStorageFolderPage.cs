namespace Asgard.Hermod.Abstractions.Models;

/// <summary>可选文件夹的完整前缀及同一目录下的下一页游标。</summary>
public sealed record McpStorageFolderPage(IReadOnlyList<string> Folders, string? NextToken);
