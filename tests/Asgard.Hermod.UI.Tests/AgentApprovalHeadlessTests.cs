using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;
using static Asgard.Hermod.UI.Tests.SshTroubleshootingWorkspaceTests;

namespace Asgard.Hermod.UI.Tests;

/// <summary>SSH 确认卡使用真实模型闭环与控件，不构造 Window。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentApprovalHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light, true)]
    [InlineData("zh-Hans", UiTheme.Dark, false)]
    public Task InlineReviewShowsFullTargetAndCannotCollapseOrRunBeforeDecision(string language, UiTheme theme, bool accept) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var connection = new SshWorkspaceTestConnection(); var dialogs = new Dialogs();
            var client = new Client([Tool("one", "ssh_command", new { command = "systemctl restart nginx" }), Asgard.Hermod.Agent.AgentReply.FromContent("Reviewed the observed outcome.")]);
            using var model = new SshAssistantViewModel(client, () => connection, () => "root@production:22", dialogs, useInlineApproval: true) { Prompt = "Review a service restart" };
            var running = model.SendCommand.ExecuteAsync(); var approval = Assert.IsType<AgentApprovalRequest>(model.PendingApproval);
            Assert.Contains("root@production:22", approval.Target); Assert.Contains("systemctl restart nginx", approval.Code); Assert.Empty(connection.Commands); Assert.Empty(dialogs.Previews);
            using var bindings = new WpfBindingTrace(); var view = new SshAssistantView(); var source = new AgentSourceView(new(model, model, "Production / SSH / Console 1", "root@production:22", "terminal", () => view, () => { }, () => model.IsSourceClosed));
            var root = new DockPanel(); var caption = new AgentWindowCaption(); DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(source);
            await Layout(); var evidence = Assert.Single(WindowTestVisuals.Descendants<AgentMessageView>(root), item => item.IsTool);
            Assert.True(evidence.IsExpanded); evidence.IsExpanded = false; Assert.True(evidence.IsExpanded);
            var card = Assert.Single(WindowTestVisuals.Descendants<AgentApprovalCard>(root), item => ReferenceEquals(item.DataContext, approval));
            Assert.Equal(Visibility.Visible, card.Visibility); Assert.True(card.ActualHeight > 100); Assert.Equal(960, card.ActualWidth, 1);
            Assert.Equal(approval.Code, ((TextBox)card.FindName("OperationCode")).Text);
            Assert.True(model.IsBusy); Assert.False(model.Archive.NewCommand.CanExecute(null));
            Capture(root, $"agent-approval-{language}-{theme}"); model.Prompt = "next draft";
            if (accept) approval.ApproveCommand.Execute(null); else approval.RejectCommand.Execute(null);
            await running.WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(accept ? 1 : 0, connection.Commands.Count);
            Assert.Equal("next draft", model.Prompt); Assert.False(approval.IsPending); Assert.False(approval.ApproveCommand.CanExecute(null));
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            async Task Layout() { await WpfTestFixture.DrainDispatcherAsync(); root.Measure(new(1120, 1000)); root.Arrange(new(0, 0, 1120, 1000)); root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task StopRevokesPendingApprovalAndUnknownDispatchedActionsNeverRetry() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var client = new Client([Tool("one", "ssh_command", new { command = "systemctl restart nginx" })]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", new Dialogs(), useInlineApproval: true) { Prompt = "review" };
        var waiting = model.SendCommand.ExecuteAsync(); var approval = model.PendingApproval!; model.CancelCommand.Execute(null); await waiting.WaitAsync(TimeSpan.FromSeconds(3));
        approval.ApproveCommand.Execute(null); Assert.Empty(connection.Commands); Assert.False(approval.IsPending);
        var late = new TaskCompletionSource<Asgard.Hermod.Ssh.SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously); connection.Handler = (_, _) => late.Task;
        var secondClient = new Client([Tool("two", "ssh_command", new { command = "systemctl restart nginx" })]);
        using var second = new SshAssistantViewModel(secondClient, () => connection, () => "host", new Dialogs(), useInlineApproval: true) { Prompt = "review" };
        var running = second.SendCommand.ExecuteAsync(); second.PendingApproval!.ApproveCommand.Execute(null);
        await SshWorkspaceTests.UntilAsync(() => connection.Commands.Count == 1); second.CancelCommand.Execute(null); await running.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(Text.Get("SshAgentOutcomeUnknown"), second.Messages.Single(item => item.IsTool).Status); Assert.Single(secondClient.Requests);
        late.SetResult(new("late output", 0, false)); await WpfTestFixture.DrainDispatcherAsync(); Assert.Single(connection.Commands); Assert.Single(secondClient.Requests);
    });

    private static void Capture(FrameworkElement root, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return;
        Directory.CreateDirectory(directory); var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
