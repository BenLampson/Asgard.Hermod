namespace Asgard.Hermod.UI.ViewModels;

/// <summary>展示目录名，同时保留不经改写的完整对象前缀。</summary>
public sealed record McpStorageFolderChoice(string Prefix, string Name);
