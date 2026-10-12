using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>只替换持久化边界，用于保存失败与窗口绑定测试。</summary>
internal sealed class FileOpenRuleTestStore : IFileOpenRuleStore
{
    /// <summary>模拟不能写入且携带私有数据库信息的基础设施异常。</summary>
    internal bool FailSave { get; set; }
    /// <summary>模拟读取失败。</summary>
    internal bool FailLoad { get; set; }
    private readonly Dictionary<string, FileOpenRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<FileOpenRule> Load() => FailLoad
        ? throw new IOException("private-database-path") : _rules.Values.OrderBy(rule => rule.Extension).ToArray();
    /// <inheritdoc />
    public void Save(FileOpenRule rule, string? originalExtension = null)
    {
        if (FailSave) throw new IOException("private-database-path");
        if (_rules.ContainsKey(rule.Extension) && originalExtension != rule.Extension)
            throw Text.Exception("FileRuleDuplicate", rule.Extension);
        if (originalExtension is not null) _rules.Remove(originalExtension);
        _rules[rule.Extension] = rule;
    }
    /// <inheritdoc />
    public void Remove(string extension)
    {
        if (FailSave) throw new IOException("private-database-path");
        _rules.Remove(extension);
    }
}
