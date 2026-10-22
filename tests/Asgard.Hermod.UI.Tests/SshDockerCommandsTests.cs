using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证预览与执行一致、确认的代次边界和独立终端跟踪行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshDockerCommandsTests(WpfTestFixture fixture)
{
    /// <summary>变更已确认成功但随后刷新失败时，不能误报变更失败或暗示用户重复执行。</summary>
    [Fact]
    public Task CompletedActionRemainsSuccessfulWhenRefreshingItsStateFails() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection(); bool completed = false;
        connection.Handler = (command, token) =>
        {
            if (command.StartsWith("docker restart ", StringComparison.Ordinal))
            { completed = true; return Task.FromResult(new SshCommandResult("done", 0, false)); }
            if (completed && command == SshMonitorDetailReader.ContainerCommand) throw new IOException("private transport failure");
            return data.HandleAsync(command, token);
        };
        using var model = Create(() => connection, new Dialogs { Approval = Task.FromResult(true) });
        await model.InitializeAsync(); model.SelectedContainer = model.Containers[1];
        await model.ActionCommand.ExecuteAsync("restart");
        Assert.Contains("hermod-api", model.Output, StringComparison.Ordinal);
        Assert.Equal(Text.Get("DockerRefreshAfterActionFailed"), model.Error);
        Assert.DoesNotContain("private", model.Error, StringComparison.Ordinal);
        Assert.Single(connection.Commands, c => c.StartsWith("docker restart ", StringComparison.Ordinal));
    });

    /// <summary>打开时不替用户选择目标；选中后点击具体动作即可执行，无需通用执行按钮。</summary>
    [Fact]
    public Task ResourceFirstActionsUseOnlyTheSelectedContainer() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        var dialogs = new Dialogs { Approval = Task.FromResult(true) };
        connection.Handler = (command, token) => command.StartsWith("docker restart ", StringComparison.Ordinal)
            ? Task.FromResult(new SshCommandResult(new string('b', 64), 0, false)) : data.HandleAsync(command, token);
        using var model = Create(() => connection, dialogs);
        await model.InitializeAsync();
        var unchanged = model.Containers[0]; await model.DetectCommand.ExecuteAsync();
        Assert.Same(unchanged, model.Containers[0]);
        Assert.Null(model.SelectedContainer); Assert.False(model.ActionCommand.CanExecute("restart"));
        model.SelectedContainer = model.Containers.First(c => c.Name == "hermod-api");
        string selectedId = model.SelectedContainer.Id;
        Assert.True(model.ActionCommand.CanExecute("restart")); Assert.False(model.ActionCommand.CanExecute("start"));
        await model.ActionCommand.ExecuteAsync("restart");
        Assert.Single(connection.Commands, c => c.StartsWith("docker restart ", StringComparison.Ordinal));
        Assert.Contains(selectedId, connection.Commands.Single(c => c.StartsWith("docker restart ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(1, dialogs.Count); Assert.Contains("hermod-api", dialogs.Detail, StringComparison.Ordinal);
        Assert.Contains("hermod-api", model.Output, StringComparison.Ordinal); Assert.DoesNotContain(selectedId, model.Output, StringComparison.Ordinal);
        Assert.Equal(selectedId, model.SelectedContainer!.Id);
        var exited = model.Containers.First(c => !c.IsRunning); model.SelectedContainer = exited;
        Assert.True(model.ActionCommand.CanExecute("start")); Assert.False(model.ActionCommand.CanExecute("stop"));
        model.ContainerSearch = "hermod-api";
        Assert.Null(model.SelectedContainer); Assert.False(model.ActionCommand.CanExecute("start"));
    });

    /// <summary>监测带入的目标不会在环境读取后被第一个容器替换。</summary>
    [Fact]
    public Task DetectionPreservesTheExplicitContainer() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshMonitorTestData().CreateConnection();
        using var model = Create(() => connection, new());
        var expected = SshMonitorDetailReader.ParseContainers((await connection.ExecuteCommandAsync(SshMonitorDetailReader.ContainerCommand, CancellationToken.None)).Output)[1];
        model.Select("restart", new(expected)); await model.InitializeAsync();
        Assert.Equal(expected.Id, model.SelectedContainer!.Id);
        Assert.DoesNotContain(connection.Commands, c => c.StartsWith("docker restart ", StringComparison.Ordinal));
    });

    /// <summary>只读命令无需确认，插入不附带回车，不向执行通道派发。</summary>
    [Fact]
    public Task ReadOnlyExecutionAndInsertionUseTheExactPreview() => fixture.RunOffscreenAsync(async () =>
    {
        var previous = Text.Culture; Text.Initialize("en", CultureInfo.InvariantCulture);
        var connection = new SshMonitorTestData().CreateConnection(); var dialogs = new Dialogs();
        using var model = Create(() => connection, dialogs);
        try
        {
            await model.InitializeAsync(); model.Select("logs", model.Containers[1]);
            string preview = model.Preview; Assert.True(model.ExecuteCommand.CanExecute(null));
            string? inserted = null; model.InsertRequested += (_, command) => inserted = command;
            int count = connection.Commands.Count; model.InsertCommand.Execute(null);
            Assert.Equal(preview, inserted); Assert.DoesNotContain('\r', inserted!); Assert.DoesNotContain('\n', inserted!); Assert.Equal(count, connection.Commands.Count);
            await model.ExecuteCommand.ExecuteAsync(); Assert.Equal(preview, connection.Commands.Last());
            Assert.Equal(0, dialogs.Count); Assert.NotEmpty(model.Output); Assert.Empty(model.Error);
            Assert.Contains("ops@host:22", model.ResultSummary, StringComparison.Ordinal);
            model.TailLines = 100; Assert.Equal(preview, model.ExecutedCommand); Assert.NotEqual(preview, model.Preview);
        }
        finally { Text.Initialize(previous.Name, CultureInfo.InvariantCulture); }
    });

    /// <summary>确认期间不能从其他入口派发；取消确认不执行，重连会废弃旧确认。</summary>
    [Fact]
    public Task ApprovalIsSerialAndCannotSurviveAConnectionChange() => fixture.RunOffscreenAsync(async () =>
    {
        ISshWorkspaceConnection? current = new SshMonitorTestData().CreateConnection(); var original = (SshWorkspaceTestConnection)current;
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var dialogs = new Dialogs { Approval = approval.Task };
        using var model = Create(() => current, dialogs); await model.InitializeAsync(); model.Select("restart", model.Containers[1]);
        string preview = model.Preview; int count = original.Commands.Count;
        var execution = model.ExecuteCommand.ExecuteAsync(); await WpfTestFixture.DrainDispatcherAsync();
        Assert.True(model.IsBusy); Assert.False(model.ExecuteCommand.CanExecute(null)); Assert.False(model.InsertCommand.CanExecute(null)); Assert.False(model.DetectCommand.CanExecute(null));
        Assert.Equal(preview, dialogs.Preview); Assert.Contains("ops@host:22", dialogs.Detail, StringComparison.Ordinal);
        await model.ExecuteCommand.ExecuteAsync(); Assert.Equal(count, original.Commands.Count);
        current = new SshMonitorTestData().CreateConnection(); model.ConnectionChanged(); approval.TrySetResult(true); await execution;
        Assert.Equal(count, original.Commands.Count); Assert.Empty(((SshWorkspaceTestConnection)current).Commands); Assert.False(model.CanExecute);
    });

    /// <summary>拒绝与允许分别只执行零次和一次变更，允许后刷新列表。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ChangesRequireExplicitApproval(bool accepted) => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection(); var dialogs = new Dialogs { Approval = Task.FromResult(accepted) };
        connection.Handler = (command, token) => command.StartsWith("docker restart ", StringComparison.Ordinal) ? Task.FromResult(new SshCommandResult("done", 0, false)) : data.HandleAsync(command, token);
        using var model = Create(() => connection, dialogs); await model.InitializeAsync(); model.Select("restart", model.Containers[1]);
        string preview = model.Preview; int changes = 0; model.Changed += (_, _) => changes++;
        await model.ExecuteCommand.ExecuteAsync();
        Assert.Equal(1, dialogs.Count); Assert.Equal(accepted ? 1 : 0, connection.Commands.Count(c => c == preview)); Assert.Equal(accepted ? 1 : 0, changes);
        Assert.Empty(model.Error);
    });

    /// <summary>失败原文不显示；跟踪日志交给专用新终端，不遗留后台无限 exec。</summary>
    [Fact]
    public Task ErrorsAreSafeAndFollowingUsesTheTerminalCallback() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection(); var dialogs = new Dialogs();
        int terminalCalls = 0; string terminalCommand = "";
        using var model = new SshDockerCommandsViewModel(() => connection, () => "ops@host:22", dialogs, () => true, (plan, token) =>
        { terminalCalls++; terminalCommand = plan.Command; return Task.CompletedTask; });
        await model.InitializeAsync(); model.Select("logs", model.Containers[1]);
        connection.Handler = (command, token) => command.StartsWith("docker logs ", StringComparison.Ordinal) ? Task.FromResult(new SshCommandResult("password=private sdk failure", 1, false)) : data.HandleAsync(command, token);
        await model.ExecuteCommand.ExecuteAsync(); Assert.NotEmpty(model.Error); Assert.Empty(model.Output); Assert.DoesNotContain("private", model.Error, StringComparison.Ordinal);
        model.Follow = true; int count = connection.Commands.Count; await model.ExecuteCommand.ExecuteAsync();
        Assert.Equal(1, terminalCalls); Assert.Equal(model.Preview, terminalCommand); Assert.Equal(count, connection.Commands.Count); Assert.Contains("--follow", terminalCommand, StringComparison.Ordinal);
    });

    /// <summary>断线后完成的旧读取不回写环境和目标。</summary>
    [Fact]
    public Task LateDetectionCannotPopulateAnotherConnection() => fixture.RunOffscreenAsync(async () =>
    {
        var delayed = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new SshWorkspaceTestConnection { Handler = (_, _) => delayed.Task }; ISshWorkspaceConnection? current = connection;
        using var model = Create(() => current, new()); var detection = model.InitializeAsync();
        current = null; model.ConnectionChanged(); delayed.TrySetResult(new(SshMonitorTestData.DockerEnvironment, 0, false)); await detection;
        Assert.Null(model.Environment); Assert.Empty(model.Containers); Assert.False(model.CanExecute);
    });

    private static SshDockerCommandsViewModel Create(Func<ISshWorkspaceConnection?> connection, Dialogs dialogs) =>
        new(connection, () => "ops@host:22", dialogs, () => true, (_, _) => Task.CompletedTask);

    private sealed class Dialogs : ISshWorkspaceDialogs
    {
        internal Task<bool> Approval { get; set; } = Task.FromResult(false);
        internal int Count { get; private set; }
        internal string Preview { get; private set; } = "";
        internal string Detail { get; private set; } = "";
        public Task<bool> ConfirmCloseAsync(string name) => Task.FromResult(false);
        public Task<bool> ConfirmActionAsync(string title, string detail, string preview) { Count++; Detail = detail; Preview = preview; return Approval; }
    }
}
