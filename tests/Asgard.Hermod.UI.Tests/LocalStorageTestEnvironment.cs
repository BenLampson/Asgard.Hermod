namespace Asgard.Hermod.UI.Tests;

/// <summary>为真实 SQLite 集成测试分配独立目录，避免触碰用户的应用数据。</summary>
public sealed class LocalStorageTestEnvironment : IDisposable
{
    /// <summary>所有临时存储测试的目录边界。</summary>
    private readonly string _testRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "Asgard.Hermod.Tests", "local-storage"));

    /// <summary>创建唯一目录地址；实际目录由被测注册流程创建。</summary>
    public LocalStorageTestEnvironment()
    {
        DirectoryPath = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
    }

    /// <summary>本次测试的绝对目录。</summary>
    public string DirectoryPath { get; }

    /// <summary>提供真实 SQLite 驱动配置，路径保持相对以验证用户目录解析。</summary>
    public LocalDatabaseConfiguration CreateConfiguration(string source = "data/hermod.db") => new(
        new DatabaseConfig
        {
            Enabled = true,
            Provider = "sqlite",
            ConnectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = source,
                Pooling = false,
                DefaultTimeout = 5,
                JournalMode = SQLiteJournalModeEnum.Wal,
            }.ConnectionString,
        }, DirectoryPath);

    /// <summary>通过生产注册方法创建容器；调用方负责先释放容器再清理测试目录。</summary>
    public ServiceProvider CreateServices(LocalDatabaseConfiguration? configuration = null) =>
        new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss().AddLocalStorage(configuration ?? CreateConfiguration())
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    /// <summary>核对目录边界后清除本次测试文件；连接泄漏会使清理失败。</summary>
    public void Dispose()
    {
        string resolved = Path.GetFullPath(DirectoryPath);
        if (!resolved.StartsWith(_testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The temporary storage directory is outside the test root.");
        if (Directory.Exists(resolved))
            Directory.Delete(resolved, recursive: true);
    }
}
