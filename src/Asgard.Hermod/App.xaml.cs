using Asgard.Hermod.Agent;

namespace Asgard.Hermod;

/// <summary>
/// 承载桌面应用的生命周期。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private Lifetime.SingleInstanceCoordinator? _instance;
    private bool _sessionEnding;
    private ITrayIcon? _tray;

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
            _instance = await Lifetime.SingleInstanceCoordinator.AcquireOrActivateAsync();
            if (_instance is null) { Shutdown(); return; }
            var services = new ServiceCollection();
            services.AddHermodCore();
            services.AddHermodAgent();
            // 进程验收可隔离文件缓存；默认仍使用现有用户级目录。
            if (Environment.GetEnvironmentVariable("HERMOD_TEMPORARY_DIRECTORY") is { Length: > 0 } temporaryDirectory)
                services.AddSingleton<ITemporaryFileStorage>(_ => new LocalTemporaryFileStorage(temporaryDirectory));
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
            services.AddPostgreSql();
            services.AddSingleton<ITerminalSessionFactory, LocalTerminalSessionFactory>();
            services.AddHermodUi(() => Asgard.Hermod.UI.Windowing.DialogOwner.Current ?? MainWindow, Resources);
            services.AddObjectStorageUi(() => Asgard.Hermod.UI.Windowing.DialogOwner.Current ?? MainWindow);
            services.AddSshUi(() => Asgard.Hermod.UI.Windowing.DialogOwner.Current ?? MainWindow);
            services.AddRedisUi(() => Asgard.Hermod.UI.Windowing.DialogOwner.Current ?? MainWindow);
            services.AddMySqlUi(() => Asgard.Hermod.UI.Windowing.DialogOwner.Current ?? MainWindow);
            services.AddPostgreSqlUi(() => Asgard.Hermod.UI.Windowing.DialogOwner.Current ?? MainWindow);
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
            // AI 只恢复非敏感本机配置，API Key 每次启动均为空。
            try
            {
                var agentPreferences = provider.GetRequiredService<IAgentPreferenceStore>().Load();
                if (agentPreferences.Endpoint.Length > 0 && agentPreferences.Model.Length > 0)
                    provider.GetRequiredService<AgentConfiguration>().Update(agentPreferences.Endpoint, agentPreferences.Model, "");
            }
            catch (Exception) { /* 配置不可用不阻止启动，可在系统设置重新配置。 */ }
            // 先恢复主题，再构造外壳、窗口和通知入口，避免重启后显示默认浅色。
            provider.GetRequiredService<IThemeService>();
            // 临时文件维护失败不阻断登录或工作区；设置页可重新扫描并给出安全提示。
            try { await provider.GetRequiredService<ITemporaryFileStorage>().CleanupAsync(expiredOnly: true); }
            catch (Exception) { }
            // 外壳由窗口拥有，窗口会在关闭前异步释放终端。
            var shell = ActivatorUtilities.CreateInstance<ShellViewModel>(provider);
            var workspaceWindows = new Asgard.Hermod.UI.Windowing.WorkspaceWindowHost(shell.Workspace);
            await shell.InitializeAsync();
            ITrayIcon? tray = null;
            try { tray = new Asgard.Hermod.UI.Windowing.WindowsTrayIcon(); }
            catch (Exception) { /* 通知区域不可用时仍保留可访问主窗体。 */ }
            _tray = tray;
            var window = new MainWindow(shell, workspaceWindows, tray, provider.GetRequiredService<ITrayPreferenceStore>(), FinishExitAsync,
                closePreferences: provider.GetRequiredService<IMainWindowClosePreferenceStore>());
            window.ExitCommitted += _instance.BeginExit;
            MainWindow = window;
            window.Show();
            _instance.SetActivationHandler(() => Dispatcher.BeginInvoke(() => window.NotifyAlreadyRunning()));
            if (tray is null)
            {
                var notice = MessageDialog.ForTrayUnavailable(); notice.Owner = window; notice.ShowDialog();
            }
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
        // 系统会话结束超出清理预算时，也必须移除通知入口。
        _tray?.Dispose();
        _services?.Dispose();
        if (_instance is not null) _instance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(args);
    }

    /// <summary>窗口和同步服务释放后关闭数据库，随后终止 IPC 并释放进程所有权。</summary>
    private async Task FinishExitAsync()
    {
        if (_services is not null) { await _services.DisposeAsync(); _services = null; }
        if (_instance is not null) { await _instance.DisposeAsync(); _instance = null; }
        if (!_sessionEnding) Shutdown();
    }

    /// <summary>注销或关机不转为托盘隐藏，保持 Dispatcher 可用以等待会话安全释放。</summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs args)
    {
        _sessionEnding = true;
        base.OnSessionEnding(args);
        // Cancel=true 会否决系统注销/关机，不可把它当作异步清理的暂停开关。
        args.Cancel = false;
        try
        {
            Lifetime.SessionEndingCleanup.Handle(args, async () =>
            {
                if (MainWindow is MainWindow window) await window.RequestExitAsync(sessionEnding: true);
                else await FinishExitAsync();
            }, TimeSpan.FromSeconds(4));
        }
        catch (Exception) { /* 系统结束期间不弹出会阻塞注销的错误窗口。 */ }
    }
}
