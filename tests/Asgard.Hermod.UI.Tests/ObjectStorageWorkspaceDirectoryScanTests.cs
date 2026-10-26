using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>临时目录日期只在完整扫描后发布，取消与导航不会套用未完成或过期结果。</summary>
public sealed class ObjectStorageWorkspaceDirectoryScanTests
{
    /// <summary>正倒序可在扫描前选择或完成后切换，不重复访问服务且保留选择、勾选和未知日期末尾。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TimeOrder_ReordersCompleteResultsWithoutRescanning(int initial)
    {
        int calls = 0;
        var date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var service = new ObjectStorageTestService
        {
            ScanHandler = (_, _, _, _) =>
            {
                calls++;
                return Task.FromResult<ObjectStorageDirectoryScanResult?>(new(
                    [new("unknown/", true), new("old/", true, LastModified: date), new("new/", true, LastModified: date.AddDays(1)), new("file", false, LastModified: date)], 3));
            },
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.TimeSortDirection = initial;
        await model.ScanDirectoryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(initial == 0 ? "new/" : "old/", model.Entries[0].Key);
        var selected = model.Entries[0]; selected.IsChecked = true; model.SelectedEntry = selected;
        model.TimeSortDirection = 1 - initial;
        Assert.Equal(initial == 0 ? "old/" : "new/", model.Entries[0].Key);
        Assert.Equal("unknown/", model.Entries[2].Key); Assert.Equal("file", model.Entries[3].Key);
        Assert.Same(selected, model.SelectedEntry); Assert.True(selected.IsChecked); Assert.Equal(1, calls);
    }

    /// <summary>完整结果替换首批列表，保留已有勾选，刷新后丢弃汇总日期及扫描状态。</summary>
    [Fact]
    public async Task CompleteScan_ReplacesPagedListAndRefreshInvalidatesDates()
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("a/", true)], "next")),
            ScanHandler = (_, _, _, _) => Task.FromResult<ObjectStorageDirectoryScanResult?>(new(
                [new("z/", true, LastModified: DateTimeOffset.UtcNow), new("a/", true), new("file.txt", false)], 42)),
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        model.Entries[0].IsChecked = true;
        await model.ScanDirectoryAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["z/", "a/", "file.txt"], model.Entries.Select(entry => entry.Key));
        Assert.Equal(42, model.ScannedFiles);
        Assert.True(model.IsScanComplete);
        Assert.False(model.HasMore);
        Assert.False(model.IsScanning);
        Assert.True(model.Entries[1].IsChecked);
        Assert.Equal(Text.Get("DirectoryScanCompleted"), model.ScanStatus);
        Assert.Equal(Text.Format("LoadedFoldersFiles", 2, 1), model.ListSummary);

        await model.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.False(model.IsScanComplete);
        Assert.Equal("", model.ScanStatus);
        Assert.Null(Assert.Single(model.Entries).Entry.LastModified);
        Assert.True(model.HasMore);
        Assert.Equal(2, service.ListCalls.Count);
    }

    /// <summary>服务商忽略取消返回旧扫描，刷新或导航的当前列表也不能被覆盖。</summary>
    [Theory]
    [InlineData("refresh")]
    [InlineData("navigate")]
    [InlineData("search")]
    [InlineData("close")]
    public async Task Navigation_DiscardsLateScan(string action)
    {
        var response = new TaskCompletionSource<ObjectStorageDirectoryScanResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, prefix, _, _, _) => Task.FromResult(new ObjectStoragePage([new(prefix + "current.txt", false)])),
            ScanHandler = (_, _, _, _) => response.Task,
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var scan = model.ScanDirectoryAsync(TestContext.Current.CancellationToken);
        if (action == "refresh") await model.RefreshAsync(TestContext.Current.CancellationToken);
        else if (action == "search") await model.SearchAsync("current", TestContext.Current.CancellationToken);
        else if (action == "close") await model.CloseAsync();
        else await model.NavigateAsync(model.SelectedBucket!, "new/", TestContext.Current.CancellationToken);
        response.SetResult(new([new("stale/", true)], 100));
        await scan;
        Assert.Equal(action == "navigate" ? "new/current.txt" : "current.txt", Assert.Single(model.Entries).Key);
        Assert.Equal("", model.ScanStatus);
        Assert.False(model.IsScanning);
        Assert.False(model.IsScanComplete);
    }

    /// <summary>取消或失败保留原顺序与下一页游标，错误不能透露原始服务消息。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnfinishedScan_PreservesList(bool fail)
    {
        var service = new ObjectStorageTestService
        {
            ListObjectsHandler = (_, _, _, _, _, _) => Task.FromResult(new ObjectStoragePage([new("a/", true), new("b/", true)], "next")),
            ScanHandler = async (_, _, _, token) =>
            {
                if (fail) throw new IOException("private-token raw SDK response");
                await Task.Delay(Timeout.Infinite, token);
                return null;
            },
        };
        await using var model = ObjectStorageWorkspaceNavigationTests.Create(service);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        var original = model.Entries.ToArray();
        var scan = model.ScanDirectoryAsync(TestContext.Current.CancellationToken);
        if (!fail) model.CancelDirectoryScanCommand.Execute(null);
        await scan;
        Assert.Equal(original, model.Entries);
        Assert.True(model.HasMore);
        Assert.False(model.IsScanComplete);
        Assert.False(model.IsScanning);
        Assert.DoesNotContain("private-token", model.ScanStatus);
        Assert.Equal(Text.Get(fail ? "DirectoryScanFailed" : "DirectoryScanCanceled"), model.ScanStatus);
    }
}
