using System.Globalization;
using System.Windows.Input;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>加载真实 WPF 页面核对双语、浅深主题、键盘入口和响应式终端布局。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshLayoutTests(WpfTestFixture fixture)
{
    /// <summary>终端必须使用打包等宽主字体，不能用复合回退字体的全宽字距。</summary>
    [Fact]
    public Task TerminalUsesBundledMonospaceMetrics() => fixture.RunAsync(() =>
    {
        using var glyphs = new Asgard.Hermod.WinTerminal.Engine.Rendering.GlyphCache();
        var font = Assert.IsType<FontFamily>(Application.Current.Resources["MonoFont"]);
        glyphs.Configure(font.Source, 14, 1.0, []);
        Assert.True(glyphs.CellWidth is >= 7 and <= 10, $"Source={font.Source}; Glyph={glyphs.Primary.FontUri}; Width={glyphs.CellWidth}");
        return Task.CompletedTask;
    });
    /// <summary>所有路径的信息正文复用公共字段，切换认证后不会显示旧密钥。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task WizardRendersBothLanguagesAndThemes(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            foreach (string route in new[] { SshIds.Direct, SshIds.Jump, SshIds.Proxy })
            {
                using var model = SshWizardTests.Create(new(), new(), route);
                var window = new NewInstanceWindow(model, [new SshConfigurationContentProvider()]);
                try
                {
                    WindowPreviewCapture.Show(window); await WindowPreviewCapture.VerifyAsync(window, $"ssh-method-{route}-{language}-{theme}");
                    Assert.True(model.IsProviderStep); Assert.False(model.IsInformationStep);
                    Assert.True(WindowTestVisuals.Named(window, "ProviderList").IsVisible);
                    Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBox>(window), input => input.Name == "SshNameInput" && input.IsVisible);
                    Assert.DoesNotContain(WindowTestVisuals.Descendants<Expander>(window), item => Equals(item.Header, model.ProviderLabel));
                    model.NextCommand.Execute(null);
                    Assert.True(model.IsInformationStep);
                    SshWizardTests.Fill(model, route);
                    await WindowPreviewCapture.VerifyAsync(window, $"ssh-form-{route}-{language}-{theme}");
                    var name = Assert.IsType<TextBox>(WindowTestVisuals.Named(window, "SshNameInput"));
                    Assert.True(name.IsVisible); Assert.True(name.Focusable); Assert.True(name.IsTabStop);
                    Assert.False(WindowTestVisuals.Named(window, "ProviderList").IsVisible);
                    Assert.Contains(WindowTestVisuals.Descendants<SecretInput>(window), input => input.IsVisible);
                    Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(window), label => label.Text.Contains("test-only", StringComparison.Ordinal));
                    if (route == SshIds.Direct)
                    {
                        SshWizardTests.Field(model, "auth").Value = SshIds.PrivateKey;
                        SshWizardTests.Field(model, "key").Value = "C:/keys/device-only";
                        await WindowPreviewCapture.VerifyAsync(window, $"ssh-key-{language}-{theme}");
                        Assert.Contains(WindowTestVisuals.Descendants<Button>(window), button => button.IsVisible && Equals(button.Content, Text.Get("Browse")));
                    }
                    else
                    {
                        var editor = Assert.Single(WindowTestVisuals.Descendants<SshConnectionEditor>(window));
                        var adapter = Assert.IsType<SshConnectionEditorViewModel>(editor.DataContext);
                        if (route == SshIds.Jump) adapter.IsJumpPane = true; else adapter.IsProxyPane = true;
                        await WindowPreviewCapture.VerifyAsync(window, $"ssh-hop-{route}-{language}-{theme}");
                    }
                    Assert.Equal(language == "en" ? "Connection method" : "连接方式", model.ProviderLabel);
                }
                finally { window.Close(); }
            }
        }
        finally { themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
    });

    /// <summary>窄窗口改为覆盖详情，终端不被侧栏挤没，工具按钮可通过更多入口使用。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task TerminalDetailsResizeAndSearchPreserveOutput(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var factory = new SshTestFactory(); await using var model = SshWorkspaceTests.Create(factory);
        using var view = new SshWorkspaceView { DataContext = model };
        var window = new HermodWindow { Width = 1180, Height = 760, Content = view, Title = "SSH test" };
        try
        {
            WindowPreviewCapture.Show(window); await model.InitializeAsync();
            factory.Connections[0].Output("\x1b[32mConnected to Production SSH\x1b[0m\r\nLinux hermod 6.1.0 x86_64\r\n\r\n\x1b[36mtester@production\x1b[0m:~$ echo 你好 Hermod\r\n你好 Hermod\r\n\x1b[36mtester@production\x1b[0m:~$ ");
            await SshWorkspaceTests.UntilAsync(() => model.Session!.GetVisibleText().Contains("你好", StringComparison.Ordinal));
            model.IsDetailsOpen = true; await WindowPreviewCapture.VerifyAsync(window, $"ssh-terminal-wide-{language}-{theme}");
            var terminal = Assert.IsType<TerminalControl>(view.FindName("TerminalView"));
            var layout = Assert.IsType<WorkspacePageLayout>(view.FindName("WorkspaceLayout"));
            Assert.False(layout.IsCompact);
            Assert.Equal(56, WindowTestVisuals.Named(layout, "PART_Toolbar").ActualHeight);
            Assert.Equal(28, WindowTestVisuals.Named(layout, "PART_StatusBar").ActualHeight);
            Assert.Equal(300, WindowTestVisuals.Named(layout, "PART_Details").ActualWidth);
            Assert.True(terminal.ActualWidth > 500); Assert.False(terminal.IsReadOnly);
            var tools = Assert.IsType<WorkspaceTools>(view.FindName("SessionTools"));
            tools.Items!.Single(item => item.Id == "search").Command.Execute(null);
            var input = Assert.IsType<TextBox>(view.FindName("SearchInput")); input.Text = "你好";
            await WindowTestVisuals.UpdateLayoutAsync(window); Assert.True(input.IsVisible);
            await SshWorkspaceTests.UntilAsync(() => terminal.GetSelectedText() == "你好");
            Assert.Empty(factory.Connections[0].Inputs);
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-terminal-search-{language}-{theme}");
            window.Width = 680; window.Height = 540; await WindowPreviewCapture.VerifyAsync(window, $"ssh-terminal-narrow-{language}-{theme}");
            Assert.True(layout.IsCompact);
            // 窄屏详情使用浮层，终端正文扣除功能栏与左右内边距后仍占满剩余宽度。
            Assert.Equal(WindowTestVisuals.Named(layout, "PART_Body").ActualWidth, terminal.ActualWidth + 52 + 40, 2);
            Assert.True(terminal.ActualWidth > 560); Assert.False(Assert.IsType<Button>(view.FindName("CopyAction")).IsVisible);
            Assert.True(tools.IsVisible);
            await model.DisconnectCommand.ExecuteAsync(); await WindowPreviewCapture.VerifyAsync(window, $"ssh-terminal-ended-{language}-{theme}");
            // 缩小网格后，早期输出可能已转入历史；保留检查应覆盖历史与当前屏幕。
            Assert.True(terminal.IsReadOnly); Assert.Contains("你好 Hermod", model.Session!.GetAllText());
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
    });

    /// <summary>主机身份变更必须显式核验，取消为默认焦点且完整指纹可选择复制。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task ChangedHostKeyRequiresVerification(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var key = new TrustedHostKey("production.example", 22, "ssh-ed25519", "SHA256:" + new string('B', 43));
        var window = new SshHostKeyWindow(new(key, key with { Fingerprint = "SHA256:" + new string('A', 43) }, false));
        try
        {
            WindowPreviewCapture.Show(window); await WindowPreviewCapture.VerifyAsync(window, $"ssh-host-changed-{language}");
            var accept = Assert.Single(WindowTestVisuals.Descendants<Button>(window), button => Equals(button.Content, Text.Get("SshUpdateTrust")));
            Assert.False(accept.IsEnabled); var verification = Assert.Single(WindowTestVisuals.Descendants<CheckBox>(window));
            verification.IsChecked = true; Assert.True(accept.IsEnabled);
            Assert.Contains(WindowTestVisuals.Descendants<TextBox>(window), text => text.IsReadOnly && text.Text == key.Fingerprint);
        }
        finally { window.Close(); CultureInfo.CurrentUICulture = previous; }
    });
}
