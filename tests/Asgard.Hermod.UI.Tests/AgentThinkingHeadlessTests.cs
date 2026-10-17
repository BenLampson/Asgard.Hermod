using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用真实聊天模板离屏验证流式思考，不创建窗口或操作桌面。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentThinkingHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    public Task ThinkingExpandsSeparatelyAndFinalizesSameMessage(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var client = new StreamingClient(); var connection = new SshWorkspaceTestConnection();
            using var model = new SshAssistantViewModel(client, () => connection, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs()) { Prompt = "Analyze the evidence" };
            var view = new SshAssistantView { DataContext = model }; view.Resources.MergedDictionaries.Add(Application.Current.Resources);
            using var bindings = new WpfBindingTrace(); var pending = model.SendCommand.ExecuteAsync(); await Layout();
            Assert.Single(model.Messages); Assert.DoesNotContain(WindowTestVisuals.Descendants<AgentThinkingView>(view), item => item.Visibility == Visibility.Visible);
            model.Prompt = "next draft";
            const string reasoning = "Compare the supplied logs.\n```bash\nnever execute reasoning\n```";
            await Task.Run(async () => await client.Receive!(new("", reasoning, true, TimeSpan.FromSeconds(3)), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            await Layout(); var message = model.Messages[^1]; var panel = (AgentPanel)view.FindName("ChatPanel");
            Assert.True(message.IsStreaming); Assert.True(panel.HasStreamingMessage); Assert.Empty(message.Content); Assert.Empty(message.Commands);
            var disclosure = Assert.Single(WindowTestVisuals.Descendants<AgentThinkingView>(view), item => ReferenceEquals(item.State, message.Thinking));
            Assert.Equal(Visibility.Visible, disclosure.Visibility); Assert.False(message.Thinking.IsExpanded);
            var toggle = (ToggleButton)disclosure.FindName("ThinkingToggle"); Assert.Equal(32, toggle.ActualHeight);
            Assert.Equal(Text.Get("AgentThinkingExpand"), System.Windows.Automation.AutomationProperties.GetName(toggle));
            Capture(view, $"thinking-collapsed-{language}"); toggle.IsChecked = true; await Layout(); Assert.True(message.Thinking.IsExpanded);
            var text = (TextBox)disclosure.FindName("ReasoningText"); Assert.True(text.IsReadOnly); Assert.True(double.IsPositiveInfinity(text.MaxHeight)); Assert.Equal(ScrollBarVisibility.Disabled, text.VerticalScrollBarVisibility); Assert.Equal(reasoning, text.Text);
            text.Select(0, 7);
            await client.Receive!(new("Review the result.\n```bash\nuname -a\n```", reasoning + "\nThe available evidence is incomplete.", false, TimeSpan.FromSeconds(4)), TestContext.Current.CancellationToken);
            await Layout(); Assert.Empty(message.Commands); Assert.Equal(7, text.SelectionLength); Assert.Equal(Text.Format("AgentThinkingCompleted", 4), message.Thinking.Header);
            Capture(view, $"thinking-expanded-{language}");
            var reply = AgentReply.FromContent("Review the result.\n```bash\nuname -a\n```") with { Thinking = message.Thinking.Content, ThinkingElapsed = TimeSpan.FromSeconds(4), ModelIdentity = "actual-model" };
            client.Done.SetResult(reply); await pending; await Layout();
            Assert.Same(message, model.Messages[^1]); Assert.True(message.Thinking.IsExpanded); Assert.False(message.IsStreaming); Assert.False(message.Thinking.IsActive); Assert.False(panel.HasStreamingMessage);
            Assert.Equal("uname -a", Assert.Single(message.Commands).Command); Assert.DoesNotContain("never execute", message.OriginalContent);
            Assert.Equal("actual-model", message.ModelIdentity); Assert.Equal("next draft", model.Prompt);
            Assert.Empty(bindings.Messages); Assert.Null(PresentationSource.FromVisual(view)); Assert.Null(Window.GetWindow(view));
            async Task Layout()
            {
                view.Measure(new(960, 850)); view.Arrange(new(0, 0, 960, 850)); view.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout();
            }
        }
        finally { Text.SetApplicationCulture(null); new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); }
    });

    [Fact]
    public Task CancellationPreservesPartialThinkingAndRejectsLateUpdates() => fixture.RunOffscreenAsync(async () =>
    {
        var client = new StreamingClient(); var connection = new SshWorkspaceTestConnection();
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs()) { Prompt = "question" };
        var pending = model.SendCommand.ExecuteAsync();
        await client.Receive!(new("partial", "received thinking", true, TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken);
        var message = model.Messages[^1]; message.Thinking.IsExpanded = true; model.CancelCommand.Execute(null); await pending;
        Assert.False(message.IsStreaming); Assert.False(message.Thinking.IsActive); Assert.True(message.Thinking.IsInterrupted); Assert.True(message.Thinking.IsExpanded);
        Assert.Equal("received thinking", message.Thinking.Content); Assert.Empty(message.Commands);
        int count = model.Messages.Count;
        await client.Receive!(new("late", "late", true, TimeSpan.Zero), TestContext.Current.CancellationToken);
        Assert.Equal(count, model.Messages.Count); Assert.Equal("partial", message.Content); Assert.Equal("received thinking", message.Thinking.Content);
    });

    [Fact]
    public Task PlainTextStreamDoesNotInventThinking() => fixture.RunOffscreenAsync(async () =>
    {
        var messages = new List<AgentDisplayMessage>(); using var stream = new AgentResponseStream<AgentDisplayMessage>(() => new("", false), messages.Add);
        await stream.ReceiveAsync(new("Plain answer", "", false, TimeSpan.Zero), TestContext.Current.CancellationToken);
        Assert.False(Assert.Single(messages).Thinking.HasContent); Assert.True(messages[0].IsStreaming);
        var reply = AgentReply.FromContent("Plain answer"); stream.Complete(new(reply.Content, false), reply);
        Assert.Single(messages); Assert.False(messages[0].Thinking.HasContent); Assert.False(messages[0].IsStreaming);
    });

    private static void Capture(FrameworkElement root, string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "thinking"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
    private sealed class StreamingClient : IAgentClient
    {
        internal Func<AgentStreamUpdate, CancellationToken, ValueTask>? Receive { get; private set; }
        internal TaskCompletionSource<AgentReply> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<AgentReply> CompleteStreamingAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition>? tools, Func<AgentStreamUpdate, CancellationToken, ValueTask>? receive, CancellationToken cancellationToken = default)
        { Receive = receive; return await Done.Task.WaitAsync(cancellationToken); }
    }
}
