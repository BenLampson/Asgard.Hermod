using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>独立助手窗口覆盖真实排障、确认与取消，以及宽窄布局和键盘焦点。</summary>
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
        var connection = new SshWorkspaceTestConnection { Handler = (_, _) => result.Task };
        var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) };
        var dialogs = new SshTroubleshootingWorkspaceTests.Dialogs();
        var client = new SshTroubleshootingWorkspaceTests.Client([
            SshTroubleshootingWorkspaceTests.Tool("diagnostic", "ssh_diagnostic", new { operation = "service_status", target = "nginx" }),
            AgentReply.FromContent(language == "en" ? "The service has stopped. Review this restart command.\n```bash\nsystemctl restart nginx\n```" : "服务已停止，请确认以下重启命令。\n```bash\nsystemctl restart nginx\n```"),
        ]);
        await using var model = new SshWorkspaceViewModel(new(new("ssh", "Production SSH", "Production SSH", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct)), new SshTestConfigurationService(), factory, new SshTestDialogs(), dialogs, client);
        using var view = new SshWorkspaceView { DataContext = model };
        using var agents = new AgentWindowService(); view.Resources[typeof(IAgentWindowService)] = agents;
        var layout = Assert.IsType<WorkspacePageLayout>(view.FindName("WorkspaceLayout")); var header = Assert.IsType<WorkspaceHeader>(layout.Toolbar); header.DataContext = model; layout.Toolbar = null; header.ShowWindowControls = true;
        var window = new HermodWindow { Width = 1440, Height = 900, CaptionHeight = 44, CaptionContent = header, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await model.InitializeAsync();
            WindowTestVisuals.Invoke(Assert.IsType<Button>(view.FindName("AgentAction")));
            var chat = Assert.Single(Application.Current.Windows.OfType<AgentChatWindow>());
            var assistant = Assert.Single(WindowTestVisuals.Descendants<SshAssistantView>(chat));
            Assert.Null(chat.Owner); Assert.Same(model.Assistant, assistant.DataContext);
            Assert.False(Assert.IsType<WorkspaceSplitView>(view.FindName("AgentSplit")).IsPaneOpen);
            connection.Output("Welcome to Ubuntu\r\nroot@production:~# ");
            model.Assistant.Prompt = language == "en" ? "Diagnose why nginx is unavailable" : "排查 nginx 为什么无法访问";
            var sending = model.Assistant.SendCommand.ExecuteAsync();
            await WindowPreviewCapture.VerifyAsync(chat, $"ssh-troubleshooting-running-{language}-{theme}");
            var panel = Assert.IsType<AgentPanel>(assistant.FindName("ChatPanel"));
            Assert.Equal(48, Assert.IsType<Border>(Assert.IsType<DockPanel>(panel.FindName("ToolbarColumn")).Parent).ActualHeight);
            var composer = Assert.IsType<Border>(panel.FindName("Composer")); var send = Assert.IsType<Button>(panel.FindName("SendAction"));
            Assert.True(composer.ActualHeight >= 110); Assert.False(send.IsEnabled); Assert.True(model.Assistant.CancelCommand.CanExecute(null));
            result.SetResult(new("nginx.service - nginx web server\nActive: inactive (dead)", 3, false)); await sending;
            await WindowPreviewCapture.VerifyAsync(chat, $"ssh-troubleshooting-result-{language}-{theme}");
            Assert.False(send.IsEnabled); Assert.False(model.Assistant.IsBusy); Assert.Empty(model.Assistant.Error);
            var command = Assert.Single(model.Assistant.Messages.SelectMany(message => message.Commands));
            var executing = model.Assistant.ExecuteCommand.ExecuteAsync(command);
            await WindowPreviewCapture.VerifyAsync(chat, $"ssh-troubleshooting-approval-{language}-{theme}");
            Assert.True(model.Assistant.IsBusy); Assert.Empty(dialogs.Previews);
            var approval = Assert.IsType<AgentApprovalRequest>(model.Assistant.PendingApproval);
            Assert.Equal("cd / && sh -c 'systemctl restart nginx'", approval.Code); Assert.True(approval.IsPending);
            var card = Assert.Single(WindowTestVisuals.Descendants<AgentApprovalCard>(chat), item => item.IsVisible && ReferenceEquals(item.DataContext, approval));
            var reject = Assert.Single(WindowTestVisuals.Descendants<Button>(card), item => ReferenceEquals(item.Command, approval.RejectCommand));
            WindowTestVisuals.Invoke(reject); await executing;
            Assert.False(approval.IsPending); Assert.DoesNotContain(approval.Code, connection.Commands); Assert.Empty(dialogs.Previews);
            chat.Width = 800; chat.Height = 600;
            await WindowPreviewCapture.VerifyAsync(chat, $"ssh-troubleshooting-narrow-{language}-{theme}");
            var scroll = Assert.IsType<ScrollViewer>(panel.FindName("ConversationScroll")); Assert.True(scroll.ActualHeight >= 100);
            var origin = send.TranslatePoint(new Point(), composer); Assert.True(origin.X >= 0); Assert.True(origin.X + send.ActualWidth <= composer.ActualWidth);
            var prompt = Assert.IsType<TextBox>(panel.FindName("PromptEditor"));
            chat.Activate();
            await WindowTestVisuals.UpdateLayoutAsync(chat);
            Assert.True(prompt.Focus());
            await WindowTestVisuals.UpdateLayoutAsync(chat);
            Assert.True(prompt.IsKeyboardFocusWithin, WindowInteractionNativeMethods.DescribeFocus(chat, prompt));
            Assert.True(composer.IsKeyboardFocusWithin);
            Assert.Equal(new Thickness(1), composer.BorderThickness);
            Assert.Null(prompt.Template.FindName("FocusRing", prompt));
            await WindowPreviewCapture.VerifyAsync(chat, $"ssh-troubleshooting-focused-{language}-{theme}");
            Assert.True(prompt.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
            await WindowTestVisuals.UpdateLayoutAsync(chat);
            Assert.False(prompt.IsKeyboardFocused);
            Assert.Equal(((SolidColorBrush)composer.FindResource("LineBrush")).Color, Assert.IsType<SolidColorBrush>(composer.BorderBrush).Color);
            Assert.True(chat.IsKeyboardFocusWithin);
        }
        finally { model.Assistant.PendingApproval?.RejectCommand.Execute(null); result.TrySetResult(new("", 0, false)); agents.Dispose(); window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = old; CultureInfo.DefaultThreadCurrentUICulture = oldDefault; }
    });
}
