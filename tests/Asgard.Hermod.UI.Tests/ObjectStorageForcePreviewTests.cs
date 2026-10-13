using System.Windows.Media.Imaging;
using Asgard.Hermod.ObjectStorage.UI.Models;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>费用确认、按实际内容解码、大小上限、重试和缓存释放。</summary>
public sealed class ObjectStorageForcePreviewTests
{
    /// <summary>所有图片点击按钮才读取，已识别类型不弹窗，未知类型才询问，两者均按实际内容解码。</summary>
    [Theory]
    [InlineData("image.PNG", "application/octet-stream", true)]
    [InlineData("image.dat", "IMAGE/PNG; charset=binary", true)]
    [InlineData("image", "image/jpeg", true)]
    [InlineData("image.dat", "application/octet-stream", false)]
    [InlineData("image.svg", "image/svg+xml", false)]
    public async Task Button_PreviewsKnownImagesAndOnlyAsksForUnknownTypes(string name, string contentType, bool inferred)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service(name, contentType);
        string? path = null;
        service.DownloadHandler = async (key, destination, _, token) =>
        {
            Assert.Equal(name, key);
            path = destination;
            await File.WriteAllBytesAsync(destination, ObjectStorageForcePreviewTestData.ImageBytes(), token);
        };
        int confirmations = 0;
        var dialogs = new ObjectStorageTestDialogs
        {
            ForcePreviewHandler = (shownName, size, type) =>
            {
                confirmations++;
                Assert.Empty(service.DownloadLimits);
                Assert.Equal(name, shownName);
                Assert.Equal(839, size);
                Assert.Null(type);
                return Task.FromResult<ObjectStoragePreviewType?>(ObjectStoragePreviewType.Image);
            }
        };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, dialogs, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == name), TestContext.Current.CancellationToken);
        Assert.True(model.ForcePreviewCommand.CanExecute(null));
        Assert.True(model.ShowForcePreviewActions);
        Assert.False(model.HasImagePreview);
        Assert.Empty(service.DownloadLimits);
        Assert.Equal(Text.Get("ForcePreview"), model.ForcePreviewLabel);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.Equal(inferred ? 0 : 1, confirmations);
        Assert.Equal(FilePreviewPolicy.ImageLimit, Assert.Single(service.DownloadLimits));
        var image = Assert.IsAssignableFrom<BitmapSource>(model.ImagePreview);
        Assert.True(image.IsFrozen);
        Assert.Equal(2, image.PixelWidth);
        Assert.True(model.HasImagePreview);
        Assert.False(model.HasTextPreview);
        Assert.False(model.ShowForcePreviewActions);
        Assert.False(model.IsImagePreviewLoading);
        Assert.False(File.Exists(path));
        Assert.Equal(contentType, model.DetailContentType);
        Assert.Empty(model.Transfers);
    }

    /// <summary>取消确认或返回未知枚举不创建临时文件，也不发出内容下载。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task UnconfirmedType_DoesNotReadObject(int? selected)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        var dialogs = new ObjectStorageTestDialogs { ForcePreviewHandler = (_, _, _) => Task.FromResult((ObjectStoragePreviewType?)selected) };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, dialogs, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(model.ForcePreviewCommand.CanExecute(null));
        await model.SelectEntryAsync(model.Entries.Single(row => row.IsDirectory), TestContext.Current.CancellationToken);
        Assert.False(model.ForcePreviewCommand.CanExecute(null));
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.dat"), TestContext.Current.CancellationToken);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.Empty(service.DownloadLimits);
        Assert.Null(model.ImagePreview);
        Assert.False(model.IsImagePreviewLoading);
        Assert.False(Directory.Exists(Path.Combine(environment.DirectoryPath, "cache"))
            && Directory.EnumerateFiles(Path.Combine(environment.DirectoryPath, "cache"), "*", SearchOption.AllDirectories).Any());
    }

    /// <summary>元数据超限时不下载；替身隐藏实际大小时仍由解码入口拒绝超大文件。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SizeLimit_CannotBeBypassedByUnknownExtensionOrMetadata(bool metadataTooLarge)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service(size: metadataTooLarge ? FilePreviewPolicy.ImageLimit + 1 : 100);
        string? path = null;
        service.DownloadHandler = (_, destination, _, _) =>
        {
            path = destination;
            using var file = File.Create(destination);
            file.SetLength(FilePreviewPolicy.ImageLimit + 1);
            return Task.CompletedTask;
        };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, ObjectStorageForcePreviewTestData.ConfirmingDialogs(), environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.dat"), TestContext.Current.CancellationToken);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.Null(model.ImagePreview);
        Assert.Contains(Text.Get("FilePreviewTooLarge"), model.PreviewMessage, StringComparison.Ordinal);
        Assert.Equal(metadataTooLarge ? 0 : 1, service.DownloadLimits.Count);
        Assert.False(File.Exists(path));
        Assert.True(model.DownloadCommand.CanExecute(null));
    }

    /// <summary>损坏图片与下载异常使用安全提示；重试再次确认费用并建立新缓存。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failure_IsSafeAndRetryRequiresAnotherConfirmation(bool invalidImage)
    {
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        var paths = new List<string>();
        int confirmations = 0;
        var dialogs = new ObjectStorageTestDialogs
        {
            ForcePreviewHandler = (_, _, _) =>
        { confirmations++; return Task.FromResult<ObjectStoragePreviewType?>(ObjectStoragePreviewType.Image); }
        };
        service.DownloadHandler = async (_, path, _, token) =>
        {
            paths.Add(path);
            if (paths.Count == 1)
            {
                await File.WriteAllTextAsync(path, "secret-access-key", token);
                if (!invalidImage) throw new IOException("secret-access-key");
            }
            else await File.WriteAllBytesAsync(path, ObjectStorageForcePreviewTestData.ImageBytes(), token);
        };
        await using var model = ObjectStorageForcePreviewTestData.Model(service, dialogs, environment.Storage);
        await model.InitializeAsync(TestContext.Current.CancellationToken);
        await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.dat"), TestContext.Current.CancellationToken);
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.DoesNotContain("secret-access-key", model.PreviewMessage, StringComparison.Ordinal);
        Assert.Contains(Text.Get(invalidImage ? "ForcePreviewDecodeFailed" : "ForcePreviewFailed"), model.PreviewMessage, StringComparison.Ordinal);
        Assert.Equal(Text.Get("ForcePreviewChooseTypeAgain"), model.ForcePreviewLabel);
        Assert.True(model.ShowForcePreviewActions);
        Assert.True(model.DownloadCommand.CanExecute(null));
        Assert.False(File.Exists(paths[0]));
        await model.ForcePreviewCommand.ExecuteAsync();
        Assert.Equal(2, confirmations);
        Assert.NotEqual(paths[0], paths[1]);
        Assert.True(model.HasImagePreview);
        Assert.All(paths, path => Assert.False(File.Exists(path)));
    }
}
