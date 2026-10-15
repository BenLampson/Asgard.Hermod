using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>对照 SSH 设计稿验证真实排障过程、50px 标题、底部输入区和宽窄分屏。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshTroubleshootingLayoutTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task DiagnosticApprovalAndResultsRemainUsableAcrossLayouts(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var old = CultureInfo.CurrentUICulture; var oldDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var result = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new SshWorkspaceTestConnection { Handler = (_, _) => result.Task };
        var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) };
        var dialogs = new SshTroubleshootingWorkspaceTests.Dialogs { Pending = approval.Task };
        var client = new SshTroubleshootingWorkspaceTests.Client([
            SshTroubleshootingWorkspaceTests.Tool("diagnostic", "ssh_diagnostic", new { operation = "service_status", target = "nginx" }),
            AgentReply.FromContent(language == "en" ? "The service has stopped. Review this restart command.\n```bash\nsystemctl restart nginx\n```" : "服务已停止，请确认以下重启命令。\n```bash\nsystemctl restart nginx\n```"),
        ]);
        await using var model = new SshWorkspaceViewModel(new(new("ssh", "Production SSH", "Production SSH", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct)), new SshTestConfigurationService(), factory, new SshTestDialogs(), dialogs, client);
        using var view = new SshWorkspaceView { DataContext = model };
        var layout = Assert.IsType<WorkspacePageLayout>(view.FindName("WorkspaceLayout")); var header = Assert.IsType<WorkspaceHeader>(layout.Toolbar); header.DataContext = model; layout.Toolbar = null; header.ShowWindowControls = true;
        var window = new HermodWindow { Width = 1440, Height = 900, CaptionHeight = 72, CaptionContent = header, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await model.InitializeAsync(); model.IsAgentOpen = true;
            connection.Output("Welcome to Ubuntu\r\nroot@production:~# ");
            model.Assistant.Prompt = language == "en" ? "Diagnose why nginx is unavailable" : "排查 nginx 为什么无法访问";
            var sending = model.Assistant.SendCommand.ExecuteAsync();
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-troubleshooting-running-{language}-{theme}");
            var assistant = Assert.IsType<SshAssistantView>(view.FindName("AssistantPanel"));
            Assert.Equal(50, Assert.IsType<Border>(Assert.IsType<DockPanel>(assistant.FindName("AgentHeader")).Parent).ActualHeight);
            var composer = Assert.IsType<Border>(assistant.FindName("Composer")); var send = Assert.IsType<Button>(assistant.FindName("SendAction"));
            Assert.True(composer.ActualHeight >= 110); Assert.False(send.IsEnabled); Assert.True(model.Assistant.CancelCommand.CanExecute(null));
            result.SetResult(new("nginx.service - nginx web server\nActive: inactive (dead)", 3, false)); await sending;
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-troubleshooting-result-{language}-{theme}");
            Assert.False(send.IsEnabled); Assert.False(model.Assistant.IsBusy); Assert.Empty(model.Assistant.Error); var command = Assert.Single(model.Assistant.Messages[^1].Commands);
            var executing = model.Assistant.ExecuteCommand.ExecuteAsync(command);
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-troubleshooting-approval-{language}-{theme}");
            Assert.True(model.Assistant.IsBusy); Assert.Single(dialogs.Previews);
            approval.SetResult(false); await executing;
            window.Width = 800; window.Height = 600;
            await WindowPreviewCapture.VerifyAsync(window, $"ssh-troubleshooting-narrow-{language}-{theme}");
            var scroll = Assert.IsType<ScrollViewer>(assistant.FindName("ConversationScroll")); Assert.True(scroll.ActualHeight >= 100);
            var origin = send.TranslatePoint(new Point(), composer); Assert.True(origin.X >= 0); Assert.True(origin.X + send.ActualWidth <= composer.ActualWidth);
            var prompt = Assert.IsType<TextBox>(assistant.FindName("PromptEditor")); Assert.True(prompt.Focus());
            Assert.True(prompt.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
            Assert.True(window.IsKeyboardFocusWithin);
        }
        finally { approval.TrySetResult(false); result.TrySetResult(new("", 0, false)); window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = old; CultureInfo.DefaultThreadCurrentUICulture = oldDefault; }
    });
}
