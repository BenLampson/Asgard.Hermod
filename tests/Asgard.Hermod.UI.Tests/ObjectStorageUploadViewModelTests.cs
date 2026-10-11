namespace Asgard.Hermod.UI.Tests;

/// <summary>上传选择只构建队列，验证去重、冲突处理、取消和错误恢复。</summary>
public sealed class ObjectStorageUploadViewModelTests
{
    /// <summary>默认询问，选择入口只有显式执行才通知宿主。</summary>
    [Fact]
    public void UploadSelection_StartsEmptyAndDoesNotOpenSelectorsAutomatically()
    {
        using var model = new ObjectStorageUploadViewModel(Bucket(), "releases/");
        int files = 0, folders = 0;
        model.SelectFilesRequested += (_, _) => files++;
        model.SelectFolderRequested += (_, _) => folders++;
        Assert.Equal(ObjectStorageOverwritePolicy.Ask, model.OverwritePolicy);
        Assert.False(model.ConfirmCommand.CanExecute(null));
        Assert.Equal("assets-dev / releases", model.Location);
        Assert.Empty(model.Items);
        Assert.Null(model.Result);
        Assert.Equal(0, files);
        Assert.Equal(0, folders);
        model.SelectFilesCommand.Execute(null);
        model.SelectFolderCommand.Execute(null);
        Assert.Equal(1, files);
        Assert.Equal(1, folders);
    }

    /// <summary>重复选择同一路径不重复上传，移除后确认只返回留下的不可变队列。</summary>
    [Fact]
    public async Task UploadSelection_DeduplicatesRemovesAndReturnsConfirmedPolicy()
    {
        ObjectStorageUploadItem[] selection = [new("C:/selected/icon.png", "releases/icon.png", 2048),
            new("C:/selected/docs/readme.md", "releases/docs/readme.md", 1024)];
        using var model = new ObjectStorageUploadViewModel(Bucket(), "releases/",
            (_, _) => Task.FromResult<IReadOnlyList<ObjectStorageUploadItem>>(selection));
        await model.AddPathsAsync(["C:/selected"]);
        await model.AddPathsAsync(["C:/selected"]);
        Assert.Equal(2, model.Items.Count);
        Assert.Equal("已选 2 个文件", model.CountText);
        Assert.Equal("3 KB", model.SizeText);
        Assert.True(model.CanConfirm);
        model.RemoveCommand.Execute(model.Items[0]);
        model.OverwritePolicy = ObjectStorageOverwritePolicy.Skip;
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        model.ConfirmCommand.Execute(null);
        Assert.NotNull(model.Result);
        Assert.Equal(selection[1], Assert.Single(model.Result.Items));
        Assert.Equal(ObjectStorageOverwritePolicy.Skip, model.Result.OverwritePolicy);
        Assert.Equal(new[] { true }, results);
        model.ConfirmCommand.Execute(null);
        model.CancelCommand.Execute(null);
        Assert.Equal(new[] { true }, results);
    }

    /// <summary>不同文件目标键相同会阻止确认，用户移除冲突项后才能继续。</summary>
    [Fact]
    public async Task UploadSelection_DuplicateDestinationKeysRequireQueueCorrection()
    {
        using var model = new ObjectStorageUploadViewModel(Bucket(), "",
            (_, _) => Task.FromResult<IReadOnlyList<ObjectStorageUploadItem>>(
                [new("C:/first/config.json", "config.json", 10), new("C:/second/config.json", "config.json", 20)]));
        await model.AddPathsAsync(["C:/first/config.json", "C:/second/config.json"]);
        Assert.True(model.HasKeyConflicts);
        Assert.NotEmpty(model.Error);
        Assert.False(model.CanConfirm);
        model.ConfirmCommand.Execute(null);
        Assert.Null(model.Result);
        model.RemoveCommand.Execute(model.Items[1]);
        Assert.False(model.HasKeyConflicts);
        Assert.Empty(model.Error);
        Assert.True(model.CanConfirm);
    }

    /// <summary>目录枚举中禁止重复添加和提交，取消传递令牌并忽略迟到文件清单。</summary>
    [Fact]
    public async Task UploadSelection_CancellationIgnoresLateMappingAndBlocksDuplicates()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ObjectStorageUploadItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        int calls = 0;
        using var model = new ObjectStorageUploadViewModel(Bucket(), "", (_, token) =>
        {
            calls++;
            received = token;
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        });
        Task running = model.AddPathsAsync(["C:/selected"]);
        await model.AddPathsAsync(["C:/other"]);
        Assert.True(model.IsAdding);
        Assert.False(model.CanConfirm);
        Assert.False(model.SelectFilesCommand.CanExecute(null));
        Assert.Equal(1, calls);
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        model.CancelCommand.Execute(null);
        Assert.True(received.IsCancellationRequested);
        completion.SetResult([new("C:/late.txt", "late.txt", 4)]);
        await running;
        Assert.Empty(model.Items);
        Assert.Null(model.Result);
        Assert.False(model.CanAddPaths);
        Assert.Equal(new[] { false }, results);
    }

    /// <summary>未知文件异常不暴露内部路径，错误之后仍可重新选择。</summary>
    [Fact]
    public async Task UploadSelection_FailureIsSafeAndRetryPreservesExistingQueue()
    {
        int calls = 0;
        using var model = new ObjectStorageUploadViewModel(Bucket(), "", (_, _) => ++calls switch
        {
            2 => Task.FromException<IReadOnlyList<ObjectStorageUploadItem>>(new IOException("private-local-path")),
            _ => Task.FromResult<IReadOnlyList<ObjectStorageUploadItem>>([new("C:/selected/item.txt", "item.txt", 20)]),
        });
        await model.AddPathsAsync(["C:/selected/item.txt"]);
        await model.AddPathsAsync(["C:/unreadable"]);
        Assert.Single(model.Items);
        Assert.NotEmpty(model.Error);
        Assert.DoesNotContain("private-local-path", model.Error, StringComparison.Ordinal);
        Assert.True(model.CanAddPaths);
        await model.AddPathsAsync(["C:/selected/item.txt"]);
        Assert.Single(model.Items);
        Assert.Empty(model.Error);
    }

    /// <summary>窗口内的覆盖选项只影响返回的明确策略，没有隐藏的远程写入。</summary>
    [Fact]
    public async Task UploadSelection_OverwriteRequiresExplicitConfirm()
    {
        using var model = new ObjectStorageUploadViewModel(Bucket(), "",
            (_, _) => Task.FromResult<IReadOnlyList<ObjectStorageUploadItem>>([new("C:/item.txt", "item.txt", 1)]));
        await model.AddPathsAsync(["C:/item.txt"]);
        model.OverwritePolicy = ObjectStorageOverwritePolicy.Overwrite;
        Assert.Null(model.Result);
        model.ConfirmCommand.Execute(null);
        Assert.NotNull(model.Result);
        Assert.Equal(ObjectStorageOverwritePolicy.Overwrite, model.Result.OverwritePolicy);
    }

    /// <summary>测试容器无需访问真实网络服务。</summary>
    private static ObjectStorageBucket Bucket() => new("assets-dev", "cn-hangzhou");
}
