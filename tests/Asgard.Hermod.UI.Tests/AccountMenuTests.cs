using System.Globalization;
using Asgard.Hermod.UI.Views.Shell;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 WPF 菜单按 Pencil 用户区的登录状态、主题和键盘语义工作。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AccountMenuTests(WpfTestFixture fixture)
{
    /// <summary>未登录点击直达登录，已登录才展开可读身份、账户信息与退出菜单。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task AccountMenu_MatchesDesignAndExecutesAccountActions(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        HermodWindow? window = null;
        ContextMenu? popup = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var previews = new ContextMenuPreviewScope();
            var accounts = new AccountUiTestService { Username = "hermod-admin" };
            var editor = new AccountTestEditor();
            var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
            await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), new FakeInstanceEditor(),
                new FakeTerminalSessionFactory(), themes, accounts: accounts, accountEditor: editor);
            var title = new TitleBarView { DataContext = shell };
            window = new HermodWindow { CaptionContent = title, DataContext = shell, Width = 1100, Height = 180 };
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var control = Assert.Single(WindowTestVisuals.Descendants<AccountMenu>(title));
            var button = Assert.IsType<Button>(control.FindName("AccountButton"));
            popup = button.ContextMenu;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(1, editor.SignIns); Assert.False(popup.IsOpen);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<IconView>(control.FindName("AccountChevron")).Visibility);
            await WindowPreviewCapture.VerifyAsync(window, $"account-local-{theme}-{language}");

            await accounts.SignInAsync();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal("he", shell.AccountInitials);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<IconView>(control.FindName("LocalDeviceIcon")).Visibility);
            Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(control.FindName("AvatarInitials")).Visibility);
            using (var focus = new KeyboardFocusPreviewScope())
            {
                focus.ShowKeyboard(button);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                var adorner = Assert.Single(KeyboardFocusPreviewScope.Adorners(button));
                var ring = Assert.Single(WindowTestVisuals.Descendants<Border>(adorner));
                Assert.Equal(new Thickness(0, 0, 0, 1), ring.BorderThickness);
                focus.ShowMouseFocus(button);
                Assert.Empty(KeyboardFocusPreviewScope.Adorners(button));
            }
            await WindowPreviewCapture.VerifyAsync(window, $"account-signed-in-{theme}-{language}");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(popup.IsOpen);
            var chrome = Assert.IsType<Border>(popup.Template.FindName("MenuChrome", popup));
            Assert.Equal(280, chrome.ActualWidth);
            Assert.Equal(new CornerRadius(8), chrome.CornerRadius);
            var targetBounds = WindowTestVisuals.ScreenBounds(button);
            var menuBounds = WindowTestVisuals.ScreenBounds(chrome);
            Assert.InRange(Math.Abs(targetBounds.Right - menuBounds.Right), 0, 1);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(popup), text => text.Text == "hermod-admin");
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(popup), text => text.Text == Text.Get("SignedIn"));
            var actions = popup.Items.OfType<MenuItem>().Where(item => item.Command is not null).ToArray();
            Assert.Equal(2, actions.Length);
            Assert.Equal(Text.Get("AccountInformation"), actions[0].Header);
            Assert.Equal(Text.Get("SignOut"), actions[1].Header);
            ContextMenuPreviewScope.Save(popup, $"account-menu-{theme}-{language}");
            await ContextMenuPreviewScope.InvokeAsync(actions[0]);
            Assert.Equal(1, editor.Opened); Assert.False(popup.IsOpen);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WpfTestFixture.DrainDispatcherAsync();
            await ContextMenuPreviewScope.InvokeAsync(actions[1]);
            Assert.Null(accounts.Account); Assert.False(popup.IsOpen);
            Assert.Equal(Visibility.Visible, Assert.IsType<IconView>(control.FindName("LocalDeviceIcon")).Visibility);
        }
        finally
        {
            if (popup is not null) popup.IsOpen = false;
            window?.Close(); CultureInfo.CurrentUICulture = previous;
            new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light);
        }
    });
}
