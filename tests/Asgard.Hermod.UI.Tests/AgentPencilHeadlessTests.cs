using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>独立助手使用真实控件离屏验收；从不构造 Window 或创建 PresentationSource。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentPencilHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, 1120, 1000, 96)]
    [InlineData("en", UiTheme.Dark, 1120, 1000, 96)]
    [InlineData("en", UiTheme.Light, 640, 1000, 96)]
    [InlineData("zh-Hans", UiTheme.Dark, 1440, 1000, 96)]
    [InlineData("en", UiTheme.Dark, 3840, 2160, 96)]
    [InlineData("zh-Hans", UiTheme.Light, 1920, 1080, 192)]
    public Task SourceToolbarComposerAndEvidenceMatchPencil(string language, UiTheme theme, double width, double height, double dpi) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var client = new Client(); var connection = new SshWorkspaceTestConnection();
            using var model = new SshAssistantViewModel(client, () => connection, () => "root@prod-web-01:22", new SshTroubleshootingWorkspaceTests.Dialogs());
            using var bindings = new WpfBindingTrace();
            var view = new SshAssistantView(); var source = new AgentSourceView(new(new object(), model, "Production / SSH / Console 1", "root@prod-web-01:22", "terminal", () => view, () => { }, () => model.IsSourceClosed));
            var root = new DockPanel(); var caption = new AgentWindowCaption(); DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(source);
            await Layout(); var panel = (AgentPanel)view.FindName("ChatPanel"); var toolbar = (DockPanel)panel.FindName("ToolbarColumn"); var composer = (Border)panel.FindName("Composer");
            Assert.Equal(dpi / 96, VisualTreeHelper.GetDpi(panel).DpiScaleX);
            Assert.Equal(44, caption.ActualHeight); Assert.Equal(new Point(0, 44), source.TranslatePoint(new(), root));
            Assert.Equal(128, panel.TranslatePoint(new(), root).Y); Assert.Equal(48, ((Border)toolbar.Parent).ActualHeight);
            double expected = Math.Min(1120, width - (width < 800 ? 48 : 160));
            Assert.Equal(expected, toolbar.ActualWidth, 1); Assert.Equal(expected, composer.ActualWidth, 1);
            Assert.Equal(height - 170, ((StackPanel)panel.FindName("ComposerColumn")).TranslatePoint(new(), root).Y, 1);
            Assert.Equal(128, composer.ActualHeight, 1); Capture(root, $"agent-empty-{language}-{theme}-{width}", dpi);
            model.Prompt = "Check service health"; var sending = model.SendCommand.ExecuteAsync();
            Assert.Single(model.Messages); Assert.True(model.Messages[0].IsUser); Assert.Empty(model.Prompt); Assert.True(model.IsBusy);
            model.Prompt = "next draft"; await Layout(); Capture(root, $"agent-waiting-{language}-{theme}-{width}", dpi);
            client.Pending.SetResult(AgentReply.FromContent("The selected snapshot is insufficient to establish a root cause.") with { ModelIdentity = "model-used-for-this-response" }); await sending;
            Assert.Equal("next draft", model.Prompt); Assert.Equal("model-used-for-this-response", model.Messages[^1].ModelIdentity); Assert.Equal("Check service health", model.Messages[^1].Question);
            var tool = new SshAssistantMessage("uname -a\nLinux production", false, 0, false, "SshAgentDiagnosticStep", true); model.Messages.Insert(1, tool);
            await Layout(); var evidence = Assert.Single(WindowTestVisuals.Descendants<AgentMessageView>(root), item => item.IsTool);
            Assert.True(panel.PresentationMessages[1].ShowToolTurnHeader); Assert.Equal("model-used-for-this-response", panel.PresentationMessages[1].ModelIdentity);
            Assert.True(panel.PresentationMessages[^1].HideMessageIdentity);
            var turnHeader = Assert.Single(WindowTestVisuals.Descendants<DockPanel>(root), item => item.Tag is "AgentToolTurnHeader" && item.Visibility == Visibility.Visible);
            Assert.Equal(24, turnHeader.ActualHeight); Assert.True(turnHeader.TranslatePoint(new(), root).Y < evidence.TranslatePoint(new(), root).Y);
            Assert.False(evidence.IsExpanded); evidence.IsExpanded = true; await Layout(); Assert.True(tool.IsExpanded);
            var answer = Assert.Single(WindowTestVisuals.Descendants<AgentMessageView>(root), item => !item.IsUser && !item.IsTool && !item.IsError);
            Assert.Equal(16, Assert.Single(WindowTestVisuals.Descendants<MarkdownView>(answer)).FontSize);
            var userMessage = Assert.Single(WindowTestVisuals.Descendants<AgentMessageView>(root), item => item.IsUser);
            Assert.Equal(15, Assert.Single(WindowTestVisuals.Descendants<MarkdownView>(userMessage)).FontSize);
            Assert.Equal(12, Assert.Single(WindowTestVisuals.Descendants<MarkdownView>(evidence)).FontSize);
            var actions = (StackPanel)answer.FindName("AnswerActions"); Assert.Equal(Visibility.Visible, actions.Visibility); Assert.Equal(28, actions.ActualHeight);
            var copy = (Button)answer.FindName("CopyAnswer"); var helpful = (Button)answer.FindName("Helpful"); var unhelpful = (Button)answer.FindName("Unhelpful");
            Assert.Equal(14, copy.ActualWidth); Assert.Equal(60, helpful.TranslatePoint(new(), actions).X);
            helpful.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(AgentMessageFeedback.Helpful, model.Messages[^1].Feedback);
            unhelpful.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(AgentMessageFeedback.Unhelpful, model.Messages[^1].Feedback);
            unhelpful.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(AgentMessageFeedback.None, model.Messages[^1].Feedback);
            helpful.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var restore = (Button)answer.FindName("RestoreQuestion"); Assert.False(restore.IsEnabled);
            model.Prompt = ""; await Layout(); WindowTestVisuals.Invoke(restore); Assert.Equal("Check service health", model.Prompt); Assert.Single(client.Requests);
            model.Prompt = "next draft"; await Layout();
            Capture(root, $"agent-answer-{language}-{theme}-{width}", dpi);
            var old = model.Archive.ActiveThread; model.Archive.NewCommand.Execute(null); Assert.Empty(model.Messages); model.Archive.ActiveThread = old; await Layout();
            Assert.True(tool.IsExpanded); Assert.Equal("next draft", model.Prompt); Assert.Single(client.Requests); Assert.Equal(AgentMessageFeedback.Helpful, model.Messages[^1].Feedback);
            model.CloseSource(); await Layout(); Assert.False(((Button)source.FindName("LocateButton")).IsEnabled); Assert.False(model.SendCommand.CanExecute(null));
            Assert.Equal(3, model.Messages.Count); Capture(root, $"agent-source-closed-{language}-{theme}-{width}", dpi);
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            async Task Layout()
            {
                await WpfTestFixture.DrainDispatcherAsync(); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
                // 模板产生的原生视觉子树完成后再传播目标 DPI，保证实际字形也在同一缩放下重排。
                VisualTreeHelper.SetRootDpi(root, new(1, 1)); VisualTreeHelper.SetRootDpi(root, new(dpi / 96, dpi / 96));
                root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task FailureRecoveryAndArchiveNeverOverwriteNextDraftOrSendAutomatically() => fixture.RunOffscreenAsync(async () =>
    {
        var client = new Client(); var connection = new SshWorkspaceTestConnection(); using var model = new SshAssistantViewModel(client, () => connection, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs()) { Prompt = "original question" };
        var pending = model.SendCommand.ExecuteAsync(); Assert.Empty(model.Prompt); model.Prompt = "next question"; model.CancelCommand.Execute(null); await pending;
        Assert.Equal("next question", model.Prompt); Assert.Equal("original question", model.Messages[^1].RecoveryPrompt);
        Assert.False(model.RestoreDraftCommand.CanExecute(model.Messages[^1].RecoveryPrompt));
        model.Prompt = ""; model.RestoreDraftCommand.Execute(null); Assert.Equal("original question", model.Prompt);
        var original = model.Archive.ActiveThread; model.Archive.NewCommand.Execute(null); model.Prompt = "another draft";
        model.Archive.ActiveThread = original; Assert.Equal("original question", model.Prompt); Assert.Equal(2, model.Messages.Count);
        model.CloseSource(); model.Archive.ActiveThread = model.Archive.Threads[1]; Assert.Equal("another draft", model.Prompt);
        Assert.False(model.Archive.NewCommand.CanExecute(null)); Assert.Single(client.Requests);
    });

    [Fact]
    public Task FrozenLogRangeIsExactBoundedAndUnaffectedByRefresh() => fixture.RunOffscreenAsync(async () =>
    {
        var client = new Client(); var connection = new SshWorkspaceTestConnection { Handler = (_, _) => Task.FromResult(new SshCommandResult("first\nselected ERROR\nlast\n", 0, false)) };
        using var document = new SshContainerLogViewModel(new(new(new string('a', 64), "worker", "worker", "running", "healthy", "")), "host", connection, client);
        await document.RefreshCommand.ExecuteAsync(); var model = document.Assistant;
        model.Selection = "selected ERROR"; model.SelectionStart = 6; model.UseSelection = true;
        await model.AnalyzeCommand.ExecuteAsync(); Assert.Empty(client.Requests); Assert.NotEmpty(model.Prompt); var attachment = model.Attachment!;
        Assert.Equal(2, attachment.FromLine); Assert.Equal(2, attachment.ToLine); Assert.Equal(6, attachment.StartCharacter); Assert.Equal(20, attachment.EndCharacter);
        connection.Handler = (_, _) => Task.FromResult(new SshCommandResult("different snapshot", 0, false)); await document.RefreshCommand.ExecuteAsync();
        Assert.Same(attachment, model.Attachment); Assert.Equal("selected ERROR", model.Attachment!.Text);
        var pending = model.SendCommand.ExecuteAsync(); Assert.Empty(model.Prompt); model.Prompt = "next"; model.CancelCommand.Execute(null); await pending;
        Assert.Same(attachment, model.Messages[0].Attachment); Assert.Equal("next", model.Prompt); Assert.Single(client.Requests);
        model.Selection = "not in snapshot"; model.SelectionStart = 0; Assert.False(model.PrepareAttachment(true)); Assert.Same(attachment, model.Attachment);
        var bounded = SshLogAttachment.Freeze(new("doc", "host", "id", "name", "source", "head\n" + new string('x', 40000), DateTimeOffset.UtcNow, 500, false));
        Assert.Equal(32000, bounded.Text.Length); Assert.True(bounded.IsTruncated); Assert.Equal(8005, bounded.StartCharacter); Assert.Equal(2, bounded.FromLine);
    });

    [Fact]
    public Task ExplicitEntriesBindAndLocateTheirOriginalConsoleWithoutDesktopWindows() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) };
        await using var workspace = SshWorkspaceTests.Create(factory); await workspace.InitializeAsync();
        using var view = new SshWorkspaceView { DataContext = workspace }; var windows = new Windows(); view.Resources[typeof(IAgentWindowService)] = windows;
        var first = workspace.ActiveConsole!; var action = (Button)view.FindName("AgentAction"); action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await workspace.NewSessionCommand.ExecuteAsync(); var second = workspace.ActiveConsole!; action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(first, windows.Requests[0].Source); Assert.Same(second, windows.Requests[1].Source);
        Assert.Same(first.Assistant, windows.Requests[0].Conversation); windows.Requests[0].Locate(); Assert.Same(first, workspace.ActiveConsole);
        Assert.False(((WorkspaceSplitView)view.FindName("AgentSplit")).IsPaneOpen); Assert.Null(Window.GetWindow(view)); Assert.Null(PresentationSource.FromVisual(view));
        await workspace.DisconnectCommand.ExecuteAsync(); Assert.True(windows.Requests[0].IsClosed()); Assert.True(windows.Requests[1].IsClosed());
        Assert.Empty(Application.Current.Windows.Cast<Window>());
    });

    private sealed class Windows : IAgentWindowService
    {
        internal List<AgentWindowRequest> Requests { get; } = [];
        public void Open(AgentWindowRequest request) => Requests.Add(request);
    }
    private sealed class Client : IAgentClient
    {
        internal TaskCompletionSource<AgentReply> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<AgentMessage[]> Requests { get; } = [];
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) { Requests.Add(messages.ToArray()); return Pending.Task; }
        public Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default) => CompleteAsync(messages, cancellationToken);
    }
    private static void Capture(FrameworkElement root, string name, double dpi = 96)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return;
        Directory.CreateDirectory(directory); var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * dpi / 96), (int)(root.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name + (dpi == 96 ? "" : "-200pct") + ".png")); encoder.Save(stream);
    }
}
