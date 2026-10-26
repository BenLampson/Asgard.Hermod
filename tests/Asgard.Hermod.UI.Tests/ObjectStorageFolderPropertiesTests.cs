using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>属性窗口必须显式开始，失败可重试，取消、关闭和迟到回调不能发布属性。</summary>
public sealed class ObjectStorageFolderPropertiesTests
{
    /// <summary>实际命令入口开始扫描，打开窗口本身不读取远端。</summary>
    [Fact]
    public async Task StartCommand_PublishesCompletePropertiesOnlyAfterExplicitStart()
    {
        int calls = 0;
        using var model = new ObjectStorageFolderPropertiesViewModel(new("bucket", "region"), "a/", (_, _) =>
        {
            calls++;
            return Task.FromResult(new ObjectStorageFolderStatistics(3, 2, 1034));
        });
        Assert.Equal(0, calls); Assert.False(model.IsComplete);
        await model.StartCommand.ExecuteAsync();
        Assert.Equal(1, calls); Assert.True(model.IsComplete); Assert.False(model.IsRunning);
        Assert.Equal(Text.Format("FolderStatisticsContains", 3, 2), model.Contains);
        Assert.Contains(1034.ToString("N0"), model.Size);
        Assert.False(model.StartCommand.CanExecute(null));
    }

    /// <summary>取消与关闭都取消传入令牌，即使忽略取消返回也不得发布结果。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrClose_DiscardsLateResult(bool close)
    {
        var response = new TaskCompletionSource<ObjectStorageFolderStatistics>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken readToken = default;
        using var model = new ObjectStorageFolderPropertiesViewModel(new("bucket", "region"), "a/", (_, token) =>
        {
            readToken = token; return response.Task;
        });
        var scan = model.StartCommand.ExecuteAsync();
        Assert.True(model.IsRunning);
        if (close) model.CancelCommand.Execute(null); else model.StopCommand.Execute(null);
        Assert.True(readToken.IsCancellationRequested);
        response.SetResult(new(5, 2, 10));
        await scan;
        Assert.False(model.IsComplete);
        if (!close) { Assert.Equal(Text.Get("FolderStatisticsCanceled"), model.Status); Assert.True(model.StartCommand.CanExecute(null)); }
    }

    /// <summary>原始响应和凭据不得出现在属性窗口错误中，失败后可重新开始。</summary>
    [Fact]
    public async Task Failure_IsSafeAndAllowsRetry()
    {
        using var model = new ObjectStorageFolderPropertiesViewModel(new("bucket", "region"), "", (_, _) =>
            throw new IOException("credential=secret raw SDK response"));
        await model.StartCommand.ExecuteAsync();
        Assert.Equal(Text.Get("FolderStatisticsFailed"), model.Status);
        Assert.False(model.IsComplete); Assert.True(model.StartCommand.CanExecute(null));
    }

    /// <summary>工具栏使用当前前缀，菜单使用所点行，普通文件不提供目录统计。</summary>
    [Fact]
    public async Task PropertiesCommand_CapturesExactDirectory()
    {
        var prefixes = new List<string>();
        var service = new ObjectStorageTestService();
        var dialogs = new ObjectStorageTestDialogs
        {
            FolderPropertiesHandler = (_, prefix, _, _) => { prefixes.Add(prefix); return Task.CompletedTask; },
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.NavigateAsync(model.SelectedBucket!, "root/", TestContext.Current.CancellationToken);
        await model.FolderPropertiesCommand.ExecuteAsync();
        await model.FolderPropertiesCommand.ExecuteAsync(new ObjectStorageEntryViewModel(new("root/sub/", true), () => { }));
        Assert.Equal(["root/", "root/sub/"], prefixes);
        Assert.False(model.FolderPropertiesCommand.CanExecute(new ObjectStorageEntryViewModel(new("file", false), () => { })));
    }
}
