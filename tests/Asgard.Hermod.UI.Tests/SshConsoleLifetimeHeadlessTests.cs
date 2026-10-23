using System.Reflection;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>实例传输、各 PTY 和本机阅读状态的独立生命周期。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshConsoleLifetimeHeadlessTests(WpfTestFixture fixture)
{
    /// <summary>显示新控制台会同步绑定并调整缓冲，PTY 订阅必须早于这次首屏布局。</summary>
    [Fact]
    public Task FirstSynchronousLayoutReachesRemotePty() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new SshTestFactory();
        await using var model = SshWorkspaceTests.Create(factory);
        using var terminal = new Asgard.Hermod.WinTerminal.TerminalControl();
        terminal.Measure(new(1500, 650)); terminal.Arrange(new(0, 0, 1500, 650));
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.Session)) terminal.Session = model.Session;
        };
        await model.InitializeAsync();
        Assert.True(model.Session!.Buffer.Columns > 100);
        Assert.Equal((model.Session.Buffer.Columns, model.Session.Buffer.Rows), factory.Connections[0].Size);
    });

    /// <summary>关闭或断开会先释放 SDK；随后发生的 PTY 状态回调不能再读取其断线属性。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingDoesNotReadTransportPropertiesAfterDisposal(bool disconnectFirst) => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection { RejectDisconnectedAfterDispose = true };
        var model = SshWorkspaceTests.Create(Factory(connection));
        try
        {
            await model.InitializeAsync(); await model.NewSessionCommand.ExecuteAsync();
            if (disconnectFirst) await model.DisconnectCommand.ExecuteAsync();
            await model.DisposeAsync(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(0, connection.DisposedTokenReads); Assert.Equal(1, connection.DisposeCount);
            Assert.All(connection.Consoles, console => Assert.Equal(1, console.DisposeCount));
            Assert.True(model.IsClosed);
        }
        finally { await model.DisposeAsync(); }
    });

    [Fact]
    public Task ZeroConsoles_KeepForwardAndPendingFileTransferAlive() => fixture.RunOffscreenAsync(async () =>
    {
        string target = Path.Combine(Path.GetTempPath(), "hermod-console-transfer-" + Guid.NewGuid().ToString("N") + ".txt");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new SshWorkspaceTestConnection { DownloadHandler = async (stream, token) => { await ready.Task.WaitAsync(token); await stream.WriteAsync("completed"u8.ToArray(), token); } };
        var dialogs = new TransferDialogs(target); var editor = new SshTestDialogs();
        await using var model = new SshWorkspaceViewModel(new(new("ssh", "SSH", "SSH", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct)), new SshTestConfigurationService(), Factory(connection), editor, dialogs);
        try
        {
            await model.InitializeAsync(); await model.NewSessionCommand.ExecuteAsync(); await model.AddForwardCommand.ExecuteAsync();
            await model.Files.RefreshCommand.ExecuteAsync(); model.Files.Selected = model.Files.Entries[1]; var downloading = model.Files.DownloadCommand.ExecuteAsync();
            await SshWorkspaceTests.UntilAsync(() => model.Files.Transfers.Count == 1); Assert.True(model.Files.IsBusy);
            foreach (var console in model.Consoles.ToArray()) await model.CloseConsoleCommand.ExecuteAsync(console);
            Assert.Empty(model.Consoles); Assert.True(model.IsConnected); Assert.Null(model.Session); Assert.False(downloading.IsCompleted);
            Assert.Single(model.Forwards); Assert.True(connection.ForwardHandles[0].CanRead); Assert.Equal(0, connection.DisposeCount);
            Assert.True(await model.RequestCloseAsync()); Assert.Equal((0, 1, 1), dialogs.Impact);
            await model.ToggleToolboxCommand.ExecuteAsync("files"); Assert.Equal(2, model.Files.Entries.Count);
            ready.SetResult(); await downloading; Assert.Equal("completed", await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
            Assert.Equal("SshTransferCompleted", model.Files.Transfers[0].StatusKey); Assert.True(connection.ForwardHandles[0].CanRead);
            Assert.True(await model.RequestCloseAsync()); Assert.Equal((0, 1, 0), dialogs.Impact);
        }
        finally { ready.TrySetResult(); if (File.Exists(target)) File.Delete(target); }
    });

    [Fact]
    public Task FailedOrLateConsoleCreation_PreservesExistingShellAndDisposesLateHandle() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection { ConsoleError = new InvalidOperationException("password=hidden") };
        await using var model = SshWorkspaceTests.Create(Factory(connection)); await model.InitializeAsync(); var first = model.ActiveConsole!;
        await model.NewSessionCommand.ExecuteAsync(); Assert.Single(model.Consoles); Assert.Same(first, model.ActiveConsole); Assert.True(model.IsConnected); Assert.DoesNotContain("hidden", model.ConsoleError);
        connection.ConsoleError = null; var pending = new TaskCompletionSource<ISshConsoleConnection>(TaskCreationOptions.RunContinuationsAsynchronously); connection.ConsoleHandler = _ => pending.Task;
        var creating = model.NewSessionCommand.ExecuteAsync(); Assert.True(model.IsCreatingConsole);
        first.Session.SendInput("still usable\r"u8); await SshWorkspaceTests.UntilAsync(() => connection.Consoles[0].Inputs.Count == 1);
        await model.DisconnectCommand.ExecuteAsync(); var late = new SshTestConnection(); pending.SetResult(late); await creating;
        Assert.Equal(1, late.DisposeCount); Assert.Single(model.Consoles); Assert.Same(first, model.ActiveConsole); Assert.True(first.IsReadOnly);
    });

    [Fact]
    public Task TransportLoss_PreservesEveryOldBufferAndReconnectCreatesOnlyOneNewShell() => fixture.RunOffscreenAsync(async () =>
    {
        var old = new SshWorkspaceTestConnection(); var replacement = new SshWorkspaceTestConnection(); int opened = 0;
        var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(opened++ == 0 ? old : replacement) };
        await using var model = SshWorkspaceTests.Create(factory); await model.InitializeAsync(); await model.NewSessionCommand.ExecuteAsync();
        var originals = model.Consoles.ToArray(); old.Consoles[0].Output("first history"); old.Consoles[1].Output("second history");
        await SshWorkspaceTests.UntilAsync(() => originals[0].Session.GetVisibleText().Contains("first history", StringComparison.Ordinal) && originals[1].Session.GetVisibleText().Contains("second history", StringComparison.Ordinal));
        old.LoseTransport(); await SshWorkspaceTests.UntilAsync(() => model.IsEnded);
        Assert.All(originals, console => { Assert.True(console.IsReadOnly); Assert.False(console.Session.IsDisposed); });
        Assert.False(originals[0].Assistant.ExecuteCommand.CanExecute(new SshAssistantCommand("uname -a", 0)));
        await model.ReconnectCommand.ExecuteAsync(); Assert.Equal(3, model.Consoles.Count); Assert.Same(model.Consoles[2], model.ActiveConsole); Assert.True(model.IsConnected);
        Assert.Single(replacement.Consoles); Assert.Empty(replacement.Consoles[0].Inputs); Assert.Contains("first history", originals[0].Session.GetVisibleText()); Assert.Contains("second history", originals[1].Session.GetVisibleText());
        model.ActiveConsole = originals[0]; Assert.True(model.IsReadOnly); Assert.Same(originals[0].Assistant, model.Assistant);
    });

    [Fact]
    public Task SharedTerminal_RestoresEachSessionsSelectionScrollSearchAndFont() => fixture.RunOffscreenAsync(async () =>
    {
        using var first = new Asgard.Hermod.WinTerminal.TerminalSession(); using var second = new Asgard.Hermod.WinTerminal.TerminalSession();
        first.FeedOutput(System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", Enumerable.Range(0, 100).Select(index => $"Aline{index:D3}")))); second.FeedOutput("B marker"u8);
        using var terminal = new Asgard.Hermod.WinTerminal.TerminalControl { Session = first, FontSize = 18, LineHeight = 30.6, PreserveOutputOnResize = true };
        var root = new Border { Child = terminal }; root.Measure(new(800, 400)); root.Arrange(new(0, 0, 800, 400)); root.UpdateLayout();
        terminal.FindText("Aline005"); string selection = terminal.GetSelectedText(); Assert.Equal("Aline005", selection);
        var offset = typeof(Asgard.Hermod.WinTerminal.TerminalControl).GetField("_scrollOffset", BindingFlags.Instance | BindingFlags.NonPublic)!; int firstOffset = (int)offset.GetValue(terminal)!; Assert.True(firstOffset > 0);
        int firstVisible = first.Buffer.ScrollbackCount - firstOffset;
        terminal.Session = second; terminal.FontSize = 13; terminal.LineHeight = 22.1; terminal.FindText("marker"); Assert.Equal("marker", terminal.GetSelectedText());
        terminal.Session = first; Assert.Equal(selection, terminal.GetSelectedText()); Assert.Equal(firstVisible, first.Buffer.ScrollbackCount - (int)offset.GetValue(terminal)!); Assert.Equal(18, terminal.FontSize); Assert.Equal(30.6, terminal.LineHeight);
        terminal.Session = second; Assert.Equal("marker", terminal.GetSelectedText()); Assert.Equal(13, terminal.FontSize); Assert.Equal(22.1, terminal.LineHeight);
        await WpfTestFixture.DrainDispatcherAsync(); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
    });

    private static SshTestFactory Factory(ISshConnection connection) => new() { Handler = _ => Task.FromResult(connection) };
    private sealed class TransferDialogs(string target) : ISshWorkspaceDialogs
    {
        internal (int Consoles, int Forwards, int Transfers)? Impact { get; private set; }
        public Task<bool> ConfirmCloseAsync(string instanceName) => Task.FromResult(true);
        public Task<bool> ConfirmInstanceCloseAsync(string instanceName, int consoles, int forwards, int transfers)
        { Impact = (consoles, forwards, transfers); return Task.FromResult(true); }
        public string? ChooseDownload(string name) => target;
    }
}
