namespace Asgard.Hermod.UI.Tests;

/// <summary>签名链接验证有效期、主动操作、错误脱敏与关闭后的迟到结果。</summary>
public sealed class ObjectStorageTemporaryLinkDialogViewModelTests
{
    /// <summary>打开窗体不会生成或复制，支持十五分钟与一小时并在修改有效期后撤销旧链接。</summary>
    [Fact]
    public async Task TemporaryLink_RequiresExplicitGenerateAndCopyForSelectedLifetime()
    {
        var durations = new List<TimeSpan>();
        var copied = new List<string>();
        using var model = new ObjectStorageTemporaryLinkDialogViewModel("releases/config.json", lifetime =>
        {
            durations.Add(lifetime);
            return Task.FromResult("https://example.test/config?signature=private");
        }, text => { copied.Add(text); return true; });
        Assert.Empty(durations);
        Assert.Empty(copied);
        Assert.Empty(model.Link);
        Assert.Equal(TimeSpan.FromMinutes(15), model.SelectedLifetime.Value);
        Assert.False(model.CopyCommand.CanExecute(null));
        await model.GenerateCommand.ExecuteAsync();
        Assert.Equal(new[] { TimeSpan.FromMinutes(15) }, durations);
        Assert.Empty(copied);
        model.SelectedLifetime = model.Lifetimes[1];
        Assert.Empty(model.Link);
        Assert.False(model.CopyCommand.CanExecute(null));
        await model.GenerateCommand.ExecuteAsync();
        Assert.Equal(new[] { TimeSpan.FromMinutes(15), TimeSpan.FromHours(1) }, durations);
        Assert.Empty(copied);
        model.CopyCommand.Execute(null);
        Assert.Equal(model.Link, Assert.Single(copied));
        Assert.Equal("已复制链接。", model.CopyStatus);
    }

    /// <summary>未知异常用固定说明，适配器白名单消息可安全展示并支持重试。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryLink_FailureOnlyDisplaysSafeMessage(bool adapterError)
    {
        int calls = 0;
        using var model = new ObjectStorageTemporaryLinkDialogViewModel("private.json", _ =>
        {
            if (++calls > 1)
                return Task.FromResult("https://example.test/safe-link");
            Exception error = adapterError ? new ObjectStorageException("当前身份无权生成此对象的临时链接。")
                : new InvalidOperationException("credential-from-sdk-exception");
            return Task.FromException<string>(error);
        }, _ => true);
        await model.GenerateCommand.ExecuteAsync();
        Assert.NotEmpty(model.Error);
        Assert.DoesNotContain("credential-from-sdk-exception", model.Error, StringComparison.Ordinal);
        if (adapterError)
            Assert.Equal("当前身份无权生成此对象的临时链接。", model.Error);
        Assert.Empty(model.Link);
        Assert.Null(model.GenerateCommand.LastException);
        await model.GenerateCommand.ExecuteAsync();
        Assert.Empty(model.Error);
        Assert.Equal("https://example.test/safe-link", model.Link);
    }

    /// <summary>禁止重复生成，关闭窗体后不呈现迟到 URL 或执行复制。</summary>
    [Fact]
    public async Task TemporaryLink_CancelIgnoresLateGenerationAndBlocksDuplicateRequests()
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, copies = 0;
        using var model = new ObjectStorageTemporaryLinkDialogViewModel("private.json", _ =>
        {
            calls++;
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }, _ => { copies++; return true; });
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        Task generating = model.GenerateCommand.ExecuteAsync();
        await model.GenerateCommand.ExecuteAsync();
        Assert.True(model.IsGenerating);
        Assert.Equal(1, calls);
        model.CancelCommand.Execute(null);
        completion.SetResult("https://example.test/private-signed-url");
        await generating;
        model.CopyCommand.Execute(null);
        Assert.Empty(model.Link);
        Assert.Empty(model.Error);
        Assert.Equal(0, copies);
        Assert.False(model.GenerateCommand.CanExecute(null));
        Assert.Equal(new[] { false }, results);
    }

    /// <summary>有效期的异步版本发生变化时，旧结果不被显示为新选择的有效期。</summary>
    [Fact]
    public async Task TemporaryLink_LifetimeChangeIgnoresOldUrl()
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new ObjectStorageTemporaryLinkDialogViewModel("private.json", _ => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), _ => true);
        Task generating = model.GenerateCommand.ExecuteAsync();
        model.SelectedLifetime = model.Lifetimes[1];
        completion.SetResult("https://example.test/expired-selection");
        await generating;
        Assert.Empty(model.Link);
        Assert.Equal(TimeSpan.FromHours(1), model.SelectedLifetime.Value);
        Assert.True(model.GenerateCommand.CanExecute(null));
    }

    /// <summary>剪贴板失败不会假报成功，重试后仅成功提示由实际结果决定。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryLink_ClipboardFailureIsSafeAndRetryable(bool throws)
    {
        int copies = 0;
        using var model = new ObjectStorageTemporaryLinkDialogViewModel("private.json",
            _ => Task.FromResult("https://example.test/private-signature"), _ =>
            {
                if (++copies > 1)
                    return true;
                if (throws)
                    throw new InvalidOperationException("clipboard-internal-error");
                return false;
            });
        await model.GenerateCommand.ExecuteAsync();
        model.CopyCommand.Execute(null);
        Assert.Empty(model.CopyStatus);
        Assert.NotEmpty(model.Error);
        Assert.DoesNotContain("clipboard-internal-error", model.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("private-signature", model.Error, StringComparison.Ordinal);
        model.CopyCommand.Execute(null);
        Assert.Equal("已复制链接。", model.CopyStatus);
        Assert.Empty(model.Error);
    }
}
