using System.Text;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>集中维护名称、颜色、层级和子树计数规则，避免窗口各自复制验证。</summary>
internal static class TagHierarchy
{
    /// <summary>路径分隔符不能出现在单个标签名中；规范化后再验证同级唯一性。</summary>
    public static TagEditDraft ValidateEdit(string? tagId, TagEditDraft draft, IReadOnlyList<StoredTag> tags)
    {
        ArgumentNullException.ThrowIfNull(draft);
        string name = (draft.Name ?? "").Trim().Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Contains('/') || name.Any(char.IsControl))
            throw Text.Exception("TagNamesMustContain180CharactersAndNoSlashesOrControlCharacters");
        if (tagId is not null)
            EnsureEditable(tagId, tags);
        string? parentId = string.IsNullOrWhiteSpace(draft.ParentId) ? null : draft.ParentId;
        if (parentId is null && string.Equals(name, "Environment", StringComparison.OrdinalIgnoreCase))
            throw Text.Exception("EnvironmentIsABuiltInTagGroupUseADifferentNameForCustomRootTags");
        if (parentId is not null)
        {
            StoredTag? parent = tags.FirstOrDefault(tag => tag.Id == parentId);
            if (parent is null)
                throw Text.Exception("TheSelectedParentTagDoesNotExistRefreshAndTryAgain");
            if (parent.IsBuiltIn)
                throw Text.Exception("BuiltInEnvironmentTagsCannotBeParentsOfCustomTags");
            if (tagId is not null && GetSubtree(tagId, tags).Contains(parentId))
                throw Text.Exception("ATagCannotBeMovedUnderItselfOrOneOfItsDescendants");
        }
        if (tags.Any(tag => tag.Id != tagId && tag.ParentId == parentId &&
            string.Equals(NameKey(tag.Name), NameKey(name), StringComparison.Ordinal)))
            throw Text.Exception("ATagWithThisNameAlreadyExistsUnderThisParentNamesAreCaseInsensitive");
        return new TagEditDraft(name, parentId, NormalizeColor(draft.Color));
    }

    /// <summary>内置标签和不存在的标签都不允许通过编辑或删除入口修改。</summary>
    public static void EnsureEditable(string tagId, IReadOnlyList<StoredTag> tags)
    {
        StoredTag? tag = tags.FirstOrDefault(value => value.Id == tagId);
        if (tag is null)
            throw Text.Exception("TheTagDoesNotExistRefreshAndTryAgain");
        if (tag.IsBuiltIn)
            throw Text.Exception("BuiltInEnvironmentTagsCannotBeEditedOrDeleted");
    }

    /// <summary>固定大小写键配合数据库唯一索引，跨并发窗口仍保持同级唯一性。</summary>
    public static string NameKey(string name) => name.Normalize(NormalizationForm.FormC).ToUpperInvariant();

    /// <summary>将短颜色展开并统一为大写六位十六进制，拒绝任意 UI 表达式。</summary>
    private static string NormalizeColor(string? value)
    {
        string color = (value ?? "").Trim();
        if ((color.Length != 4 && color.Length != 7) || color[0] != '#' ||
            !color[1..].All(Uri.IsHexDigit))
            throw Text.Exception("ColorsMustUseRGBOrRRGGBBHexadecimalFormat");
        return color.Length == 4
            ? $"#{color[1]}{color[1]}{color[2]}{color[2]}{color[3]}{color[3]}".ToUpperInvariant()
            : color.ToUpperInvariant();
    }

    /// <summary>迭代遍历子树，集合去重也能避免损坏数据中的循环导致无限递归。</summary>
    public static HashSet<string> GetSubtree(string tagId, IReadOnlyList<StoredTag> tags)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { tagId };
        var pending = new Queue<string>();
        pending.Enqueue(tagId);
        while (pending.TryDequeue(out string? parentId))
            foreach (StoredTag child in tags.Where(tag => tag.ParentId == parentId))
                if (result.Add(child.Id)) pending.Enqueue(child.Id);
        return result;
    }

    /// <summary>由稳定父标识现算展示路径，标签改名后实例不需要重写关联。</summary>
    public static IReadOnlyList<ManagedTag> ToSnapshots(IReadOnlyList<StoredTag> tags,
        IReadOnlyDictionary<string, IReadOnlyList<string>> instanceTags)
    {
        var lookup = tags.ToDictionary(tag => tag.Id, StringComparer.Ordinal);
        return tags.Select(tag =>
        {
            var names = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            StoredTag? current = tag;
            while (current is not null)
            {
                if (!visited.Add(current.Id))
                    throw Text.Exception("TagHierarchyDataIsCorruptedCheckTheLocalDatabase");
                names.Push(current.Name);
                current = current.ParentId is not null ? lookup.GetValueOrDefault(current.ParentId) : null;
            }
            HashSet<string> subtree = GetSubtree(tag.Id, tags);
            // 一个实例即使同时关联父标签和子标签，也只计数一次。
            int count = instanceTags.Count(pair => pair.Value.Any(subtree.Contains));
            return new ManagedTag(tag.Id, tag.Name, tag.ParentId, tag.Color, tag.IsBuiltIn,
                (tag.IsBuiltIn ? "Environment/" : "") + string.Join('/', names), count);
        }).OrderByDescending(tag => tag.IsBuiltIn).ThenBy(tag => tag.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
