namespace Asgard.Hermod;

/// <summary>
/// 承载桌面应用的生命周期。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    /// <summary>装配宿主服务，完成本地数据库初始化后再打开主窗口。</summary>
    protected override async void OnStartup(StartupEventArgs args)
    {
        // 捕获 Windows 用户的显示语言，避免手动偏好覆盖后失去系统语言来源。
        var systemCulture = System.Globalization.CultureInfo.CurrentUICulture;
        Text.Initialize(null, systemCulture);
        base.OnStartup(args);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var services = new ServiceCollection();
            services.AddHermodCore();
            services.AddWindowsSecretProtection();
            services.AddLocalStorage(LocalDatabaseConfiguration.Load(args.Args));
            var configuration = new Asgard.Core.SystemConfig.AsgardConfigurationBuilder()
                .AddYamlFile(Path.Combine(AppContext.BaseDirectory, "app.yaml"))
                .AddEnvironmentVariables("HERMOD_").AddCommandLine(args.Args).Build();
            services.AddHermodIdentity(configuration.LoadSection<HermodIdentityOptions>("identity"));
            services.AddObjectStorage();
            services.AddAliyunOss();
            services.AddTencentCos();
            services.AddVolcengineTos();
            services.AddAwsS3();
            services.AddMinio();
            services.AddS3Connector<S3CompatibleStorageProvider>();
            services.AddSsh();
            services.AddRedis();
            services.AddMySql();
            services.AddSingleton<ITerminalSessionFactory, LocalTerminalSessionFactory>();
            services.AddHermodUi(() => MainWindow, Resources);
            services.AddObjectStorageUi(() => MainWindow);
            services.AddSshUi(() => MainWindow);
            services.AddRedisUi(() => MainWindow);
            services.AddMySqlUi(() => MainWindow);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
            _services = provider;
            // SQLite 驱动执行同步 I/O，在后台完成连接和迁移，保持 Dispatcher 可用。
            await Task.Run(() => provider.GetRequiredService<LocalDatabaseInitializer>().Initialize());
            var languageStore = provider.GetRequiredService<ILanguagePreferenceStore>();
            Text.Initialize(languageStore.Load(), systemCulture);
            Text.SetApplicationCulture(Text.Culture);
            // 临时文件维护失败不阻断登录或工作区；设置页可重新扫描并给出安全提示。
            try { await provider.GetRequiredService<ITemporaryFileStorage>().CleanupAsync(expiredOnly: true); }
            catch (Exception) { }
            // 外壳由窗口拥有，窗口会在关闭前异步释放终端。
            var shell = ActivatorUtilities.CreateInstance<ShellViewModel>(provider);
            await shell.InitializeAsync();
            MainWindow = new MainWindow(shell);
            MainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception exception)
        {
            // 持久化不可用时给出明确反馈，不能让用户在无法保存的状态下继续操作。
            var dialog = new MessageDialog(Text.Get("UnableToStartHermod"),
                Text.Error(exception, "CannotStartHermodCheckLocalStorageConfigurationAndPermissions"));
            dialog.ShowDialog();
            Shutdown(1);
        }
    }

    /// <summary>窗口已经等待会话关闭后，释放数据库连接池和其他宿主服务。</summary>
    protected override void OnExit(ExitEventArgs args)
    {
        _services?.Dispose();
        base.OnExit(args);
    }
}
