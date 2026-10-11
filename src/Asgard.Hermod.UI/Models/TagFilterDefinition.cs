namespace Asgard.Hermod.UI.Models;

/// <summary>标签筛选的只读树定义；路径也是筛选条件的稳定标识。</summary>
public sealed class TagFilterDefinition
{
    /// <summary>从实际标签数据建立节点，后代数量由子树计算。</summary>
    public TagFilterDefinition(string name, string path, IReadOnlyList<TagFilterDefinition>? children = null,
        string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Name = name;
        Path = path;
        Children = Array.AsReadOnly((children ?? []).ToArray());
        Count = Children.Sum(child => 1 + child.Count);
        Color = color;
    }

    /// <summary>当前层显示的标签名称。</summary>
    public string Name { get; }

    /// <summary>完整标签路径；父路径包含其下所有后代路径。</summary>
    public string Path { get; }

    /// <summary>保持数据源顺序的直接子标签。</summary>
    public IReadOnlyList<TagFilterDefinition> Children { get; }

    /// <summary>实际后代标签总数，不包括当前节点。</summary>
    public int Count { get; }
    /// <summary>来自标签目录的自定义颜色，虚拟分组不指定颜色。</summary>
    public string? Color { get; }
}
