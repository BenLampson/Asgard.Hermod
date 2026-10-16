using System.Globalization;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实模板离屏验收日志窗口；所有网络和模型响应使用替身，不创建 HWND。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshContainerLogTests(WpfTestFixture fixture)
{
    /// <summary>日志来源、48/40/24 区域及搜索在两种语言与主题、窄宽尺寸下可用。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task WindowContentRendersWithoutShowingDesktop(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var connection = Connection(); var client = new Client();
        connection.Handler = (_, _) => Task.FromResult(new SshCommandResult(PreviewLog(), 0, false));
        using var document = new SshContainerLogViewModel(Container(), "root@prod-web-01:22", connection, client);
        using var view = new SshContainerLogView { DataContext = document };
        var caption = new SshLogCaption { DataContext = document };
        var root = new DockPanel(); DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(view);
        try
        {
            Assert.Empty(connection.Commands); Assert.Null(Window.GetWindow(view));
            await document.RefreshCommand.ExecuteAsync();
            await LayoutAsync(root, 1440, 900);
            var output = view.LogOutput;
            Assert.Equal(48, caption.ActualHeight); Assert.Equal(40, ((Border)view.FindName("LogToolbar")).ActualHeight);
            Assert.Equal(new Point(0, 88), ((CodeEditor)view.FindName("LogEditor")).TranslatePoint(new(), root));
            Assert.Null(PresentationSource.FromVisual(root));
            Capture(root, $"container-logs-initial-{language}-{theme}");
            Assert.Equal(document.LogText, output.Text); Assert.True(output.ActualWidth >= view.ActualWidth * 0.5, $"Reader width: {output.ActualWidth}"); Assert.True(output.ActualHeight >= view.ActualHeight * 0.6, $"Reader height: {output.ActualHeight}");
            Assert.True(output.IsReadOnly); Assert.Equal(Text.Get("SshLogTail500"), Assert.IsType<TextBlock>(((ComboBoxItem)Assert.IsType<ComboBox>(view.FindName("TailRange")).SelectedItem).Content).Text);
            var search = Assert.IsType<TextBox>(view.FindName("SearchInput")); search.Text = "ERROR";
            Assert.Equal("ERROR", output.SelectedText); Assert.Equal(Text.Format("SshSearchMatches", 1, 1), Assert.IsType<TextBlock>(view.FindName("MatchCount")).Text);
            search.Text = "no matching line"; Assert.Empty(output.SelectedText);
            search.Text = "ERROR";
            document.Assistant.PrepareAttachment(false); document.Assistant.Prompt = "What failed?"; await document.Assistant.SendCommand.ExecuteAsync();
            await LayoutAsync(root, 1440, 900); Capture(root, $"container-logs-{language}-{theme}");
            document.IsAiOpen = false; await LayoutAsync(root, 1080, 760);
            Assert.True(output.ActualWidth >= 900); Capture(root, $"container-logs-reading-{language}-{theme}");
            document.IsAiOpen = true; await LayoutAsync(root, 1080, 760);
            Assert.True(output.ActualWidth >= 450); Assert.True(output.ActualHeight >= 300);
            Capture(root, $"container-logs-compact-{language}-{theme}");
            Assert.All(Application.Current.Windows.Cast<Window>(), item => Assert.False(item.IsVisible));
        }
        finally { themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
    });

    /// <summary>独立文档使用完整 ID，刷新失败隐藏 stderr 且保留最后成功快照。</summary>
    [Fact]
    public Task RefreshRetainsSnapshotAndHidesUnsafeFailures() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = Connection(); using var document = new SshContainerLogViewModel(Container(), "host", connection, null);
        document.TailLines = 1000; await document.RefreshCommand.ExecuteAsync();
        Assert.Equal(SshMonitorDetailReader.ContainerLogCommand(document.Container.Id, 1000), Assert.Single(connection.Commands));
        Assert.NotNull(document.CapturedAt); string snapshot = document.LogText;
        connection.Handler = (_, _) => Task.FromResult(new SshCommandResult("password=private stderr", 1, false));
        await document.RefreshCommand.ExecuteAsync(); Assert.Equal(snapshot, document.LogText);
        Assert.Equal(Text.Get("SshMonitorLogFailed"), document.Status); Assert.DoesNotContain("password", document.Status);
        document.TailLines = -1; Assert.Equal(1000, document.TailLines);
        Assert.True(document.Assistant.PrepareAttachment(false)); document.Assistant.Prompt = "question"; await document.Assistant.SendCommand.ExecuteAsync();
        Assert.Equal(Text.Get("AgentConfigurationRequired"), document.Assistant.Error);
    });

    /// <summary>断线立即解除不合作的旧读取，迟到日志不能覆盖已保留快照。</summary>
    [Fact]
    public Task DisconnectCancelsLateLogWithoutClosingSnapshotAnalysis() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = Connection(); using var document = new SshContainerLogViewModel(Container(), "host", connection, new Client());
        await document.RefreshCommand.ExecuteAsync(); string snapshot = document.LogText;
        var late = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Handler = (_, _) => late.Task;
        var reading = document.RefreshCommand.ExecuteAsync(); Assert.True(document.IsLoading);
        document.Disconnect(); await reading; late.SetResult(new("wrong late log", 0, false));
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(snapshot, document.LogText); Assert.False(document.RefreshCommand.CanExecute(null));
        Assert.Equal(Text.Get("SshLogDisconnected"), document.Status);
        Assert.True(document.Assistant.PrepareAttachment(false)); document.Assistant.Prompt = "Analyze retained snapshot"; await document.Assistant.SendCommand.ExecuteAsync();
        Assert.Equal(2, document.Assistant.Messages.Count);
    });

    /// <summary>进行中的模型请求固定证据，刷新及编辑草稿不会覆盖上下文或用户新输入。</summary>
    [Fact]
    public Task AiUsesFrozenSelectionAndPreservesNewDraft() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = Connection(); var client = new Client();
        using var document = new SshContainerLogViewModel(Container(), "host", connection, client);
        await document.RefreshCommand.ExecuteAsync();
        document.Assistant.UseSelection = true; document.Assistant.Prompt = "Why?";
        Assert.False(document.Assistant.SendCommand.CanExecute(null));
        document.Assistant.Selection = "[ERROR] Connection timed out after 30s"; document.Assistant.SelectionStart = document.LogText.IndexOf(document.Assistant.Selection, StringComparison.Ordinal); Assert.True(document.Assistant.PrepareAttachment(true));
        var reply = new TaskCompletionSource<AgentReply>(TaskCreationOptions.RunContinuationsAsynchronously); client.Pending = reply.Task;
        var analysis = document.Assistant.SendCommand.ExecuteAsync(); document.Assistant.Prompt = "new draft";
        using var data = JsonDocument.Parse(client.Requests[0][^1].Content);
        Assert.Equal("[ERROR] Connection timed out after 30s", data.RootElement.GetProperty("context").GetProperty("log").GetString());
        connection.Handler = (_, _) => Task.FromResult(new SshCommandResult("new log", 0, false));
        await document.RefreshCommand.ExecuteAsync(); reply.SetResult(AgentReply.FromContent("Analysis of old evidence.")); await analysis;
        Assert.Equal("new draft", document.Assistant.Prompt); Assert.Equal("new log", document.LogText);
        Assert.Equal(2, document.Assistant.Messages.Count); Assert.Equal(0, client.Tools);
    });

    /// <summary>上下文超过上限时明确告知并只发送末尾，关闭会取消模型请求。</summary>
    [Fact]
    public Task AnalysisIsBoundedAndDisposalCancelsUncooperativeClient() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = Connection(); var client = new Client(); using var document = new SshContainerLogViewModel(Container(), "host", connection, client);
        connection.Handler = (_, _) => Task.FromResult(new SshCommandResult(new string('x', 40000) + "tail evidence", 0, true));
        await document.RefreshCommand.ExecuteAsync(); Assert.True(document.Assistant.PrepareAttachment(false)); Assert.True(document.Assistant.Attachment!.IsTruncated);
        document.Assistant.Prompt = "analyze"; var late = new TaskCompletionSource<AgentReply>(TaskCreationOptions.RunContinuationsAsynchronously); client.Pending = late.Task;
        var pending = document.Assistant.SendCommand.ExecuteAsync();
        using var data = JsonDocument.Parse(client.Requests[0][^1].Content);
        string context = data.RootElement.GetProperty("context").GetProperty("log").GetString()!;
        Assert.Equal(LogAnalysisConversation.ContextLimit, context.Length); Assert.EndsWith("tail evidence", context);
        document.Dispose(); await pending; late.SetResult(AgentReply.FromContent("late answer"));
        Assert.Single(document.Assistant.Messages); Assert.False(document.Assistant.SendCommand.CanExecute(null));
    });

    /// <summary>工作区复用同一容器窗口；监测筛选、切换和断线不替换已打开文档。</summary>
    [Fact]
    public Task WorkspaceReusesDocumentsAndKeepsContainerIdentity() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) };
        await using var workspace = SshWorkspaceTests.Create(factory); await workspace.InitializeAsync();
        workspace.Monitor.IsPaused = true; workspace.Monitor.Section = "docker"; await workspace.Monitor.RefreshDetailsCommand.ExecuteAsync();
        var opened = new List<SshContainerLogViewModel>(); workspace.LogWindowRequested += (_, document) => opened.Add(document);
        var first = workspace.Monitor.Containers[0]; var second = workspace.Monitor.Containers[1];
        await workspace.Monitor.ReadLogsCommand.ExecuteAsync(first); await workspace.Monitor.ReadLogsCommand.ExecuteAsync(first);
        Assert.Same(opened[0], opened[1]);
        await workspace.Monitor.ReadLogsCommand.ExecuteAsync(second); Assert.NotSame(opened[0], opened[2]);
        workspace.Monitor.SelectedContainer = second; workspace.Monitor.Section = "network";
        Assert.Same(first, opened[0].Container); Assert.Same(second, opened[2].Container);
        await workspace.DisconnectCommand.ExecuteAsync(); Assert.All(opened, document => Assert.False(document.IsConnected));
        Assert.All(opened, workspace.ReleaseLogDocument);
    });

    private static SshMonitorContainerItem Container() => new(new(new string('a', 64), "bridge-worker", "bridge-worker:latest", "running", "healthy", ""));
    /// <summary>只在离屏截图中使用的日志样本，生产代码不注入演示数据。</summary>
    private static string PreviewLog() => string.Join('\n', Enumerable.Range(0, 42).Select(index =>
        new DateTimeOffset(2026, 10, 15, 15, 21, 2, TimeSpan.Zero).AddSeconds(index).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) +
        (index == 14 ? " [ERROR] Connection timed out after 30s" : index == 15 ? " [WARN] Retrying in 5s" : " [INF] Worker processed event; queue=bridge.events status=completed")));
    private static SshWorkspaceTestConnection Connection() => new()
    {
        Handler = (_, _) => Task.FromResult(new SshCommandResult("2026-10-15T15:21:15Z [INF] Worker started\n2026-10-15T15:21:16Z [ERROR] Connection timed out after 30s\n2026-10-15T15:21:17Z [WARN] Retrying in 5s\n", 0, false))
    };
    private static async Task LayoutAsync(FrameworkElement view, double width, double height)
    {
        await WpfTestFixture.DrainDispatcherAsync(); view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height)); view.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync();
    }
    private static void Capture(FrameworkElement view, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR"); if (directory is null) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
    private sealed class Client : IAgentClient
    {
        internal List<AgentMessage[]> Requests { get; } = [];
        internal Task<AgentReply>? Pending { get; set; }
        internal int Tools { get; private set; }
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default)
        { Requests.Add(messages.ToArray()); return Pending ?? Task.FromResult(AgentReply.FromContent(Text.Culture.Name == "zh-Hans" ? "## 发现连接超时\n\n**15:21:16** 的错误随后触发了重试：\n\n> [ERROR] Connection timed out after 30s\n\n### 下一步排查\n\n1. 检查目标服务是否可达。\n2. 对照同时间段的服务端日志。\n\n现有片段不足以判断具体根因。" : "## Connection timeout\n\nThe error at **15:21:16** is followed by a retry:\n\n> [ERROR] Connection timed out after 30s\n\n### Next checks\n\n1. Check service reachability.\n2. Correlate server logs from the same time.\n\nThis snapshot does not establish the root cause.")); }
        public Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default)
        { Tools++; throw new InvalidOperationException(); }
    }
}
