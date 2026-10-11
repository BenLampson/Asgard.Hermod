namespace Asgard.Hermod.UI.Models;

/// <summary>将实例提供的斜杠标签路径转换为选择器的分组树。</summary>
internal static class TagFilterTreeBuilder
{
    /// <summary>保留来源中的分组顺序，并合并大小写不同的同一路径。</summary>
    public static IReadOnlyList<TagFilterDefinition> Create(IEnumerable<string> paths)
    {
        string[][] segments = paths.Select(NormalizePath).Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(path => path.Split('/')).ToArray();
        return BuildLevel(segments, 0, "");
    }

    /// <summary>清除多余分隔符和空白，使树节点与实例过滤使用相同路径。</summary>
    internal static string NormalizePath(string path) => string.Join('/',
        path.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    /// <summary>同一层的路径片段形成节点；子节点只保留尚未结束的路径。</summary>
    private static IReadOnlyList<TagFilterDefinition> BuildLevel(IEnumerable<string[]> paths, int depth,
        string parentPath) => paths.Where(parts => parts.Length > depth)
        .GroupBy(parts => parts[depth], StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            string path = parentPath.Length == 0 ? group.Key : $"{parentPath}/{group.Key}";
            return new TagFilterDefinition(group.Key, path, BuildLevel(group, depth + 1, path));
        }).ToArray();
}
