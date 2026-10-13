using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证设置账户入口、实际绑定与跨线程账号变化，不访问登录服务或真实账号数据。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SettingsAccountWindowTests(WpfTestFixture fixture)
{
    /// <summary>已登录打开设置时显示真实账号，切换和退出后同步更新，关闭解除订阅。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Account_TracksSessionAndRemainsUsableAtSmallSize(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        SettingsWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var accounts = new AccountUiTestService { Username = "hermod-admin" };
            await accounts.SignInAsync();
            int opens = 0;
            using var model = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(),
                accounts: accounts, openAccount: () => { opens++; return Task.CompletedTask; });
            window = new SettingsWindow(model);
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var navigation = Assert.Single(WindowTestVisuals.Descendants<Button>(window),
                button => button.CommandParameter is SettingsCategoryViewModel { Id: "account" });
            WindowTestVisuals.Invoke(navigation);
            await WindowPreviewCapture.VerifyAsync(window, $"settings-account-{language}-{theme}");
            Assert.True(model.IsAccountSelected);
            Assert.Equal("hermod-admin", Assert.IsType<TextBlock>(window.FindName("SettingsAccountName")).Text);
            Assert.Equal(Text.Get("SyncCloudWorkspace"), Assert.IsType<TextBlock>(window.FindName("SettingsWorkspaceName")).Text);
            Assert.Equal(Text.Get("SignedIn"), Assert.IsType<TextBlock>(window.FindName("SettingsAccountStatus")).Text);
            var manage = Assert.IsType<Button>(window.FindName("ManageAccountButton"));
            WindowTestVisuals.Invoke(manage);
            Assert.Equal(1, opens);

            accounts.Username = "second-account-with-a-long-username-for-layout";
            await Task.Run(() => accounts.SignInAsync());
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(accounts.Username, Assert.IsType<TextBlock>(window.FindName("SettingsAccountName")).Text);
            window.Width = 800; window.Height = 540;
            await WindowPreviewCapture.VerifyAsync(window, $"settings-account-small-{language}-{theme}");
            var frame = WindowTestVisuals.Frame(window);
            Point origin = manage.TranslatePoint(new Point(), frame);
            Assert.InRange(origin.X + manage.ActualWidth, 1, frame.ActualWidth + 1);
            Assert.InRange(origin.Y + manage.ActualHeight, 1, frame.ActualHeight + 1);

            await Task.Run(() => accounts.SignOutAsync());
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(Text.Get("NotSignedIn"), Assert.IsType<TextBlock>(window.FindName("SettingsAccountStatus")).Text);
            Assert.Equal(Text.Get("LocalWorkspace"), Assert.IsType<TextBlock>(window.FindName("SettingsWorkspaceName")).Text);
            Assert.True(manage.IsEnabled);
            int notifications = 0;
            model.PropertyChanged += (_, _) => notifications++;
            window.Close();
            await Task.Run(() => accounts.SignInAsync());
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(0, notifications);
            Assert.False(model.OpenAccountCommand.CanExecute(null));
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentUICulture = previous;
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>真实服务装配保持设置入口可用，账号弹窗属于设置窗口且不会重新启动登录。</summary>
    [Fact]
    public Task SettingsEditor_OpensAccountManagementOwnedBySettings() => fixture.RunAsync(async () =>
    {
        var accounts = new AccountUiTestService { Username = "hermod-admin" };
        await accounts.SignInAsync();
        using var environment = new TemporaryStorageTestEnvironment();
        var services = new ServiceCollection();
        services.AddSingleton<IAccountService>(accounts);
        services.AddSingleton<IWorkspaceSyncService>(accounts);
        services.AddSingleton<ITagManagementService>(new TagManagementTestService());
        services.AddSingleton<ILanguagePreferenceStore>(new LanguagePreferenceTestStore());
        services.AddSingleton<IFileOpenRuleStore>(new FileOpenRuleTestStore());
        services.AddSingleton<ITemporaryFileStorage>(environment.Storage);
        services.AddHermodUi(() => null, Application.Current.Resources);
        using var provider = services.BuildServiceProvider();
        var editor = provider.GetRequiredService<ISettingsEditor>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        var deadline = DateTime.UtcNow.AddSeconds(8);
        SettingsWindow? settings = null;
        AccountWindow? account = null;
        int phase = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                Assert.True(DateTime.UtcNow < deadline, "设置账户入口没有完成交互。");
                if (phase == 0)
                {
                    settings = Application.Current.Windows.OfType<SettingsWindow>().SingleOrDefault();
                    if (settings is null) return;
                    settings.Opacity = 0;
                    var model = Assert.IsType<SettingsViewModel>(settings.DataContext);
                    model.SelectCategoryCommand.Execute(model.Categories.Single(category => category.Id == "account"));
                    Assert.True(model.IsAccountSelected);
                    phase = 1;
                    settings.Dispatcher.InvokeAsync(() => WindowTestVisuals.Invoke(Assert.IsType<Button>(settings.FindName("ManageAccountButton"))));
                }
                else
                {
                    account = Application.Current.Windows.OfType<AccountWindow>().SingleOrDefault();
                    if (account is null) return;
                    account.Opacity = 0;
                    Assert.Same(settings, account.Owner);
                    var model = Assert.IsType<AccountViewModel>(account.DataContext);
                    Assert.True(model.IsSignedIn);
                    Assert.Equal("hermod-admin", model.Username);
                    Assert.False(model.IsBusy);
                    Assert.Empty(model.Message);
                    timer.Stop();
                    account.Close(); settings!.Close();
                    finished.TrySetResult();
                }
            }
            catch (Exception error)
            {
                timer.Stop(); account?.Close(); settings?.Close();
                finished.TrySetException(error);
            }
        };
        try
        {
            timer.Start();
            await editor.OpenAsync();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally { timer.Stop(); account?.Close(); settings?.Close(); }
    });
}
