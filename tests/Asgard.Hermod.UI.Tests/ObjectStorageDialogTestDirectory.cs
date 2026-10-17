namespace Asgard.Hermod.UI.Tests;

/// <summary>文件选择测试独占的临时目录，清理前校验根目录不逃逸。</summary>
internal sealed class ObjectStorageDialogTestDirectory : IDisposable
{
    /// <summary>每个用例单独建立目录，绝不使用真实用户文件。</summary>
    internal ObjectStorageDialogTestDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "Hermod-object-dialog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>本用例唯一允许清理的目录。</summary>
    internal string Root { get; }

    /// <summary>绝对路径与创建时命名约束都通过后才递归删除。</summary>
    public void Dispose()
    {
        string resolved = Path.GetFullPath(Root);
        string temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved).StartsWith("Hermod-object-dialog-", StringComparison.Ordinal))
            throw new InvalidOperationException("测试目录超出允许清理的范围。");
        if (Directory.Exists(resolved))
            Directory.Delete(resolved, true);
    }
}
