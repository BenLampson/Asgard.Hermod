using Asgard.Hermod.Services;

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
        base.OnStartup(args);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var services = new ServiceCollection();
            services.AddLocalStorage(LocalDatabaseConfiguration.Load(args.Args));
            services.AddSingleton<ITerminalSessionFactory, LocalTerminalSessionFactory>();
            services.AddSingleton<IInstanceEditor>(provider => new InstanceEditorService(() => MainWindow,
                provider.GetRequiredService<IInstanceProviderRegistry>(),
                provider.GetRequiredService<IInstanceConfigurationService>()));
            services.AddSingleton<ISettingsEditor>(provider => new SettingsEditorService(() => MainWindow,
                provider.GetRequiredService<ITagManagementService>()));
            services.AddSingleton<IObjectStorageDialogService>(_ => new WindowsObjectStorageDialogService(() => MainWindow));
            services.AddSingleton<IInstancePageProvider, ObjectStoragePageProvider>();
            services.AddSingleton<IInstanceWorkspaceFactory, InstanceWorkspaceFactory>();
            services.AddSingleton<IThemeService>(_ => new ThemeService(Resources));
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
            _services = provider;
            // SQLite 驱动执行同步 I/O，在后台完成连接和迁移，保持 Dispatcher 可用。
            await Task.Run(() => provider.GetRequiredService<LocalDatabaseInitializer>().Initialize());
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
            var dialog = new MessageDialog("Unable to start Hermod", exception.Message);
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
