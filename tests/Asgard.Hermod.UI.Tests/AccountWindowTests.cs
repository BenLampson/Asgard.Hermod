using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证双语真实窗口、用户名展示与下载确认的业务入口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AccountWindowTests(WpfTestFixture fixture)
{
    /// <summary>未登录不能同步；登录展示 JWT 用户名；恢复必须明确确认覆盖本地数据。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task AccountWindow_RendersAndControlsSyncInBothLanguages(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        AccountWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var service = new AccountUiTestService(); await using var model = new AccountViewModel(service, service);
            window = new AccountWindow(model); WindowPreviewCapture.Show(window);
            Assert.Equal(Text.Get("AccountAndSync"), window.Title); Assert.False(model.UploadCommand.CanExecute(null));
            var signInButton = Assert.IsType<Button>(window.FindName("SignInButton"));
            Assert.Equal(Text.Get("SignInToYourAccount"), signInButton.Content);
            await model.SignInCommand.ExecuteAsync();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(Text.Get("SwitchAccount"), signInButton.Content);
            Assert.Equal(Text.Get("SignedIn"), model.AccountStatus);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), text => text.Text == "JWT-用户名");
            Assert.Equal("JWT-用户名", model.Username); model.Password = "password-for-sync";
            Assert.True(model.UploadCommand.CanExecute(null)); Assert.False(model.DownloadCommand.CanExecute(null));
            await model.UploadCommand.ExecuteAsync(); Assert.Equal(1, service.Uploads);
            model.ConfirmRestore = true; await model.DownloadCommand.ExecuteAsync(); Assert.Equal(1, service.Downloads);
            await WindowPreviewCapture.VerifyAsync(window, "account-sync-" + language);
            window.Width = 520; window.Height = 540;
            await WindowPreviewCapture.VerifyAsync(window, "account-sync-compact-" + language);
            var secret = Assert.Single(WindowTestVisuals.Descendants<SecretInput>(window));
            Assert.Equal(model.Password, secret.Value);
            await model.SignOutCommand.ExecuteAsync(); Assert.False(model.UploadCommand.CanExecute(null));
            Assert.Equal(Text.Get("NotSignedIn"), model.Username);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(Text.Get("SignInToYourAccount"), signInButton.Content);
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; }
    });

    /// <summary>外壳和账号窗口共享同一身份服务，真实绑定在登录和退出后立即刷新。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task ShellAccountMenu_RefreshesJwtUsernameAfterDialogLogin(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        Window? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var service = new AccountUiTestService();
            await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), new FakeInstanceEditor(),
                new FakeTerminalSessionFactory(), new ThemeService(Application.Current.Resources), accounts: service);
            await using var model = new AccountViewModel(service, service);
            var menu = new AccountMenu { DataContext = shell };
            window = new Window { Content = menu, Width = 440, Height = 120 };
            window.Show();
            Assert.Equal(Text.Get("Local"), shell.AccountName);
            await model.SignInCommand.ExecuteAsync();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal("JWT-用户名", shell.AccountName);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(menu), text => text.Text == "JWT-用户名");
            Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(menu.FindName("AvatarInitials")).Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<StackPanel>(menu.FindName("LocalSignIn")).Visibility);
            await model.SignOutCommand.ExecuteAsync();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(Text.Get("Local"), shell.AccountName);
            Assert.Equal(Visibility.Visible, Assert.IsType<StackPanel>(menu.FindName("LocalSignIn")).Visibility);
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; }
    });
}
