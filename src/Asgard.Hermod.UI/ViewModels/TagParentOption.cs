namespace Asgard.Hermod.UI.ViewModels;

/// <summary>标签父级候选项；空标识表示根级，显示完整路径避免重名歧义。</summary>
public sealed record TagParentOption(string? Id, string DisplayName);
