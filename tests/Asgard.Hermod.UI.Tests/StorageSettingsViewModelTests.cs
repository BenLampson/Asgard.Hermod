using System.Globalization;
using Asgard.Hermod.UI.Localization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>确认是删除调用的必要前提，结果使用真实统计并隐藏内部错误。</summary>
public sealed class StorageSettingsViewModelTests
{
    /// <summary>真实闲置文件只有确认后才删除，活动文件始终保留且统计立即更新。</summary>
    [Theory]
    [InlineData("en", false)]
    [InlineData("en", true)]
    [InlineData("zh-Hans", false)]
    [InlineData("zh-Hans", true)]
    public async Task Cleanup_RequiresConfirmationAndRefreshesActualUsage(string language, bool accept)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var environment = new TemporaryStorageTestEnvironment();
            using var active = environment.Storage.CreateFile("active.txt");
            await File.WriteAllBytesAsync(active.Path, new byte[512], TestContext.Current.CancellationToken);
            string directory = Path.Combine(environment.Storage.RootPath, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string idle = Path.Combine(directory, "idle.txt");
            await File.WriteAllBytesAsync(idle, new byte[1024], TestContext.Current.CancellationToken);
            var dialogs = new StorageSettingsTestDialogs { Accept = accept };
            using var model = new StorageSettingsViewModel(environment.Storage, dialogs);
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1536, model.Snapshot?.TotalBytes);
            Assert.Equal(2, model.Snapshot?.FileCount);
            Assert.Equal(1024, model.Snapshot?.CleanableBytes);
            Assert.True(model.CleanupCommand.CanExecute(null));
            await model.CleanupCommand.ExecuteAsync();
            Assert.Equal((1024L, 1), Assert.Single(dialogs.Confirmations));
            Assert.Equal(!accept, File.Exists(idle));
            Assert.True(File.Exists(active.Path));
            Assert.Equal(accept ? 512 : 1536, model.Snapshot?.TotalBytes);
            Assert.Equal(accept ? Text.Format("StorageCleanupCompleted", FileSizeText.Format(1024), 1, 1) : "", model.Message);
            model.OpenFolderCommand.Execute(null);
            Assert.Equal(environment.Storage.RootPath, dialogs.OpenedPath);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    /// <summary>清理失败不展示私有路径或异常原文，读取失败也不会显示虚假的零空间。</summary>
    [Fact]
    public async Task Failures_AreSafeAndLeaveRetryAvailable()
    {
        var storage = new StorageSettingsTestStorage { Inspect = _ => throw new IOException("private-path credentials") };
        var dialogs = new StorageSettingsTestDialogs { Accept = true };
        using var model = new StorageSettingsViewModel(storage, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Null(model.Snapshot);
        Assert.Equal(Text.Get("StorageUnknownSize"), model.TotalSize);
        Assert.Equal(Text.Get("StorageScanFailed"), model.Message);
        Assert.True(model.RefreshCommand.CanExecute(null));
        Assert.False(model.CleanupCommand.CanExecute(null));
        storage.Inspect = null;
        storage.Cleanup = _ => throw new IOException("private-path credentials");
        await model.RefreshCommand.ExecuteAsync();
        await model.CleanupCommand.ExecuteAsync();
        Assert.Equal(1, storage.CleanupCalls);
        Assert.Equal(Text.Get("StorageCleanupFailed"), model.Message);
        Assert.DoesNotContain("credentials", model.Message);
        Assert.True(model.RefreshCommand.CanExecute(null));
        Assert.True(model.CleanupCommand.CanExecute(null));
    }

    /// <summary>设置关闭会取消扫描，故意忽略取消的迟到服务也不能更新已经释放的模型。</summary>
    [Fact]
    public async Task Close_CancelsScanAndIgnoresLateResult()
    {
        var completion = new TaskCompletionSource<TemporaryStorageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken scanToken = default;
        var storage = new StorageSettingsTestStorage { Inspect = token => { scanToken = token; return completion.Task; } };
        using var model = new StorageSettingsViewModel(storage, new StorageSettingsTestDialogs());
        Task scan = model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(model.CleanupCommand.CanExecute(null));
        model.Dispose();
        Assert.True(scanToken.IsCancellationRequested);
        completion.SetResult(new(2048, 1, 0, 0, []));
        await scan.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Null(model.Snapshot);
        Assert.False(model.IsInitialized);
        Assert.False(model.RefreshCommand.CanExecute(null));
    }

    /// <summary>占用中的全部文件没有可清理入口，刷新后最新状态才能开启清理。</summary>
    [Fact]
    public async Task InUseOnly_DisablesCleanupWithoutConfirmation()
    {
        var storage = new StorageSettingsTestStorage { Inspect = _ => Task.FromResult(new TemporaryStorageSnapshot(100, 1, 100, 1, [])) };
        var dialogs = new StorageSettingsTestDialogs { Accept = true };
        using var model = new StorageSettingsViewModel(storage, dialogs);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.CleanupCommand.ExecuteAsync();
        Assert.Empty(dialogs.Confirmations);
        Assert.Equal(0, storage.CleanupCalls);
        Assert.False(model.CleanupCommand.CanExecute(null));
    }
}
