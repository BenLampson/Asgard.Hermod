using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>容器空状态、镜像快照和显式拉取均使用离线传输替身。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshDockerImageTests(WpfTestFixture fixture)
{
    /// <summary>后台刷新和本地筛选改变列表时，实际空状态立即更新。</summary>
    [Fact]
    public Task ContainerEmptyStateFollowsBackgroundChanges() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshMonitorTestData().CreateConnection();
        using var model = Create(connection, new()); var view = new SshDockerCommandsView { DataContext = model };
        await Layout(view); var empty = Assert.IsType<TextBlock>(view.FindName("NoContainers"));
        Assert.Equal(Visibility.Collapsed, empty.Visibility);
        await model.InitializeAsync(); var item = model.Containers[0]; await Layout(view);
        Assert.Equal(Visibility.Collapsed, empty.Visibility);
        model.UpdateResources([]); await Layout(view); Assert.Equal(Visibility.Visible, empty.Visibility);
        model.UpdateResources([item]); await Layout(view); Assert.Equal(Visibility.Collapsed, empty.Visibility);
        model.ContainerSearch = "no-such-container"; await Layout(view); Assert.Equal(Visibility.Visible, empty.Visibility); Assert.Equal(Text.Get("SshMonitorNoMatches"), empty.Text);
        model.ContainerSearch = ""; await Layout(view); Assert.Equal(Visibility.Collapsed, empty.Visibility);
    });

    /// <summary>手动加载一次快照；切换标签和名称、标签、ID 筛选都不再次请求远端。</summary>
    [Fact]
    public Task ImageSnapshotIsSearchableWithoutRepeatedRequests() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshMonitorTestData().CreateConnection(); using var model = Create(connection, new());
        Assert.Empty(connection.Commands); await model.InitializeAsync();
        Assert.True(model.HasImageSnapshot); Assert.Equal(3, model.Images.Count); Assert.False(model.ImagesLimited);
        int count = connection.Commands.Count; model.SelectToolCommand.Execute("images");
        model.ImageSearch = "hermod-api"; Assert.Single(model.ImageView.Cast<object>());
        model.ImageSearch = "alpine"; Assert.Single(model.ImageView.Cast<object>());
        model.ImageSearch = new string('b', 12); Assert.Single(model.ImageView.Cast<object>());
        model.ImageSearch = "not-present"; Assert.True(model.HasNoImages); Assert.Equal(Text.Get("SshMonitorNoMatches"), model.ImageEmptyMessage);
        model.ImageSearch = ""; Assert.False(model.HasNoImages); Assert.Equal(count, connection.Commands.Count);
        model.ConnectionChanged(); Assert.Empty(model.Images); Assert.False(model.HasImageSnapshot);
    });

    /// <summary>镜像读取失败不掩盖已加载的容器，也不泄露失败原文；刷新成功后恢复。</summary>
    [Fact]
    public Task FailedImageReadIsSafeAndDoesNotDiscardContainers() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection(); bool fail = true;
        connection.Handler = (command, token) => command == SshDockerImageReader.Command && fail
            ? Task.FromResult(new SshCommandResult("password=private daemon error", 1, false)) : data.HandleAsync(command, token);
        using var model = Create(connection, new()); await model.InitializeAsync();
        Assert.True(model.IsInitialized); Assert.NotEmpty(model.Containers); Assert.False(model.HasImageSnapshot);
        Assert.Equal(Text.Get("DockerImageReadFailed"), model.Error); Assert.DoesNotContain("private", model.Error);
        fail = false; await model.DetectCommand.ExecuteAsync(); Assert.True(model.HasImageSnapshot); Assert.Empty(model.Error);
        fail = true; await model.DetectCommand.ExecuteAsync(); Assert.Equal(3, model.Images.Count); Assert.NotEmpty(model.Error);
        fail = false; await model.DetectCommand.ExecuteAsync(); Assert.Empty(model.Error); Assert.Equal(3, model.Images.Count);
    });

    /// <summary>切换连接时取消未完成的镜像读取，旧主机的结果不能写入新会话。</summary>
    [Fact]
    public Task LateImageSnapshotCannotCrossConnections() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var old = data.CreateConnection(); ISshWorkspaceConnection? current = old;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        old.Handler = (command, token) =>
        {
            if (command != SshDockerImageReader.Command) return data.HandleAsync(command, token);
            entered.TrySetResult(); return delayed.Task;
        };
        using var model = new SshDockerCommandsViewModel(() => current, () => "host", new Dialogs(), () => true, (_, _) => Task.CompletedTask);
        var initial = model.InitializeAsync(); await entered.Task; current = data.CreateConnection(); model.ConnectionChanged();
        delayed.SetResult(new(SshMonitorTestData.Images, 0, false)); await initial;
        Assert.Empty(model.Images); Assert.False(model.HasImageSnapshot); Assert.Null(model.Environment);
        await model.InitializeAsync(); Assert.Equal(3, model.Images.Count); Assert.True(model.HasImageSnapshot);
    });

    /// <summary>拉取成功后重新读取上方的镜像表格。</summary>
    [Fact]
    public Task SuccessfulPullRefreshesImageTable() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection(); bool pulled = false;
        string added = System.Text.Json.JsonSerializer.Serialize(new { Repository = "example/api", Tag = "latest", ID = "sha256:" + new string('d', 64), CreatedSince = "1 second ago", Size = "20MB" });
        connection.Handler = (command, token) =>
        {
            if (command.StartsWith("docker pull ", StringComparison.Ordinal)) { pulled = true; return Task.FromResult(new SshCommandResult("pulled", 0, false)); }
            if (pulled && command == SshDockerImageReader.Command) return Task.FromResult(new SshCommandResult(SshMonitorTestData.Images + "\n" + added, 0, false));
            return data.HandleAsync(command, token);
        };
        using var model = Create(connection, new Dialogs { Approve = true });
        await model.InitializeAsync(); model.SelectToolCommand.Execute("images"); model.Image = "example/api:latest";
        await model.ExecuteCommand.ExecuteAsync();
        Assert.Contains(model.Images, image => image.Repository == "example/api" && image.Tag == "latest");
        Assert.Equal(2, connection.Commands.Count(command => command == SshDockerImageReader.Command));
        Assert.Empty(model.Error);
    });

    /// <summary>拉取使用镜像作为目标并要求确认；成功后刷新列表，刷新失败不会暗示重试拉取。</summary>
    [Fact]
    public Task PullRefreshesImagesAndReportsRefreshFailureSeparately() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection(); bool pulled = false;
        connection.Handler = (command, token) =>
        {
            if (command.StartsWith("docker pull ", StringComparison.Ordinal)) { pulled = true; return Task.FromResult(new SshCommandResult("pulled", 0, false)); }
            if (pulled && command == SshDockerImageReader.Command) return Task.FromResult(new SshCommandResult("private error", 1, false));
            return data.HandleAsync(command, token);
        };
        var dialogs = new Dialogs { Approve = true }; using var model = Create(connection, dialogs);
        await model.InitializeAsync(); model.SelectedContainer = model.Containers[0]; model.SelectToolCommand.Execute("images");
        model.Image = "example/api:latest"; await model.ExecuteCommand.ExecuteAsync();
        Assert.Equal(1, dialogs.Count); Assert.Single(connection.Commands, command => command.StartsWith("docker pull ", StringComparison.Ordinal));
        Assert.Contains("example/api:latest", model.ResultTarget, StringComparison.Ordinal);
        Assert.DoesNotContain(model.SelectedContainer!.Name, model.ResultTarget, StringComparison.Ordinal);
        Assert.True(model.IsOutputOpen); Assert.Contains("example/api:latest", model.Output, StringComparison.Ordinal);
        Assert.Equal(Text.Get("DockerImageRefreshAfterPullFailed"), model.Error); Assert.Equal(3, model.Images.Count);
    });

    private static SshDockerCommandsViewModel Create(ISshWorkspaceConnection connection, Dialogs dialogs) =>
        new(() => connection, () => "ops@host:22", dialogs, () => true, (_, _) => Task.CompletedTask);
    private static async Task Layout(FrameworkElement view)
    {
        view.Measure(new Size(900, 820)); view.Arrange(new Rect(0, 0, 900, 820)); view.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout();
    }
    private sealed class Dialogs : ISshWorkspaceDialogs
    {
        internal bool Approve { get; init; }
        internal int Count { get; private set; }
        public Task<bool> ConfirmCloseAsync(string instanceName) => Task.FromResult(false);
        public Task<bool> ConfirmActionAsync(string title, string detail, string preview) { Count++; return Task.FromResult(Approve); }
    }
}
