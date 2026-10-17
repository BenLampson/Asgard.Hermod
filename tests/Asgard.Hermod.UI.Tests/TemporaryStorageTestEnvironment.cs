namespace Asgard.Hermod.UI.Tests;

/// <summary>临时文件测试使用独立目录，绝不扫描或清理当前用户的真实应用缓存。</summary>
internal sealed class TemporaryStorageTestEnvironment : IDisposable
{
    private readonly LocalStorageTestEnvironment _environment = new();
    /// <summary>为测试创建可重新打开的真实文件管理服务。</summary>
    internal TemporaryStorageTestEnvironment() => Storage = new(Path.Combine(_environment.DirectoryPath, "cache"));
    /// <summary>被测服务与隔离根目录。</summary>
    internal LocalTemporaryFileStorage Storage { get; }
    /// <summary>测试根目录还可容纳必须保留的配置或工作区文件。</summary>
    internal string DirectoryPath => _environment.DirectoryPath;
    /// <summary>先关闭租约再清理经过边界验证的测试目录。</summary>
    public void Dispose() { Storage.Dispose(); _environment.Dispose(); }
}
