namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>加载 Asgard 数据库配置，将本地数据库定位到可写的用户数据目录。</summary>
public sealed class LocalDatabaseConfiguration
{
    /// <summary>验证持久化配置并解析绝对路径；此阶段不创建目录或数据库。</summary>
    public LocalDatabaseConfiguration(DatabaseConfig database, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        database.Validate();
        if (!database.Enabled)
            throw new InvalidOperationException("Hermod requires local database storage to be enabled.");
        if (!string.Equals(database.Provider, "sqlite", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hermod local storage requires the SQLite provider.");

        var connection = new SQLiteConnectionStringBuilder(database.ConnectionString);
        if (string.IsNullOrWhiteSpace(connection.DataSource)
            || string.Equals(connection.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(connection.Uri) || !string.IsNullOrEmpty(connection.FullUri))
            throw new InvalidOperationException("Hermod local storage requires a database file path.");
        if (connection.ReadOnly)
            throw new InvalidOperationException("Hermod local storage requires a writable database.");

        // 安装目录和进程工作目录可能不可写，始终以用户数据目录解析相对路径。
        dataDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Asgard", "Hermod");
        DatabasePath = Path.GetFullPath(connection.DataSource, Path.GetFullPath(dataDirectory));
        connection.DataSource = DatabasePath;
        Database = new DatabaseConfig
        {
            Enabled = true,
            Provider = "sqlite",
            ConnectionString = connection.ConnectionString,
        };
    }

    /// <summary>用于 Asgard 数据库注册的配置，连接字符串已包含绝对文件路径。</summary>
    public DatabaseConfig Database { get; }

    /// <summary>当前用户数据库文件的绝对路径。</summary>
    public string DatabasePath { get; }

    /// <summary>按 YAML、HERMOD_ 环境变量、命令行顺序加载并验证配置。</summary>
    public static LocalDatabaseConfiguration Load(IEnumerable<string> args,
        string? configurationFile = null, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var configuration = new AsgardConfigurationBuilder()
            .AddYamlFile(configurationFile ?? Path.Combine(AppContext.BaseDirectory, "app.yaml"))
            .AddEnvironmentVariables("HERMOD_")
            .AddCommandLine(args)
            .Build();
        return new LocalDatabaseConfiguration(configuration.Load<DatabaseConfig>(), dataDirectory);
    }
}
