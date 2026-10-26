using Asgard.Hermod.ObjectStorage.UI.Models;

namespace Asgard.Hermod.UI.Tests;

/// <summary>记录用户选择与确认，默认取消写操作，不使用系统对话框或剪贴板。</summary>
internal sealed class ObjectStorageTestDialogs : IObjectStorageDialogService
{
    /// <summary>属性窗口替身记录明确路径，只有测试明确开始时才调用扫描。</summary>
    internal Func<ObjectStorageBucket, string, Func<IProgress<ObjectStorageFolderStatistics>, CancellationToken, Task<ObjectStorageFolderStatistics>>, CancellationToken, Task>? FolderPropertiesHandler { get; set; }
    /// <inheritdoc />
    public Task ShowFolderPropertiesAsync(ObjectStorageBucket bucket, string prefix,
        Func<IProgress<ObjectStorageFolderStatistics>, CancellationToken, Task<ObjectStorageFolderStatistics>> scan, CancellationToken cancellationToken = default) =>
        FolderPropertiesHandler?.Invoke(bucket, prefix, scan, cancellationToken) ?? Task.CompletedTask;
    /// <summary>已明确选择的上传队列。</summary>
    internal ObjectStorageUploadSelection? UploadSelection { get; set; }
    /// <summary>单文件下载的用户路径。</summary>
    internal string? DownloadPath { get; set; }
    /// <summary>批量下载目录。</summary>
    internal string? DownloadFolder { get; set; }
    /// <summary>已经通过本地校验的目录名。</summary>
    internal string? FolderName { get; set; }
    /// <summary>删除确认的用户选择。</summary>
    internal bool ConfirmDeletion { get; set; }
    /// <summary>同名覆盖确认。</summary>
    internal bool ConfirmOverwrite { get; set; }
    /// <summary>用户实际请求生成链接的有效期，空值表示只打开后取消。</summary>
    internal TimeSpan? GenerateLifetime { get; set; }
    /// <summary>用户看到的完整删除清单。</summary>
    internal ObjectStorageDeletePlan? PresentedPlan { get; private set; }
    /// <summary>覆盖确认次数。</summary>
    internal int OverwriteConfirmations { get; private set; }
    /// <summary>明确复制的文本。</summary>
    internal List<string> CopiedTexts { get; } = [];
    /// <summary>类型与费用确认替身，默认取消，不自动授权下载。</summary>
    internal Func<string, long, ObjectStoragePreviewType?, Task<ObjectStoragePreviewType?>>? ForcePreviewHandler { get; set; }

    /// <inheritdoc />
    public Task<ObjectStorageUploadSelection?> SelectUploadAsync(ObjectStorageBucket bucket, string prefix) => Task.FromResult(UploadSelection);
    /// <inheritdoc />
    public Task<string?> SelectDownloadPathAsync(string suggestedName) => Task.FromResult(DownloadPath);
    /// <inheritdoc />
    public Task<string?> SelectDownloadFolderAsync() => Task.FromResult(DownloadFolder);
    /// <inheritdoc />
    public Task<string?> RequestFolderNameAsync(ObjectStorageBucket bucket, string prefix) => Task.FromResult(FolderName);
    /// <inheritdoc />
    public Task<bool> ConfirmDeleteAsync(ObjectStorageBucket bucket, string prefix, ObjectStorageDeletePlan plan)
    {
        PresentedPlan = plan;
        return Task.FromResult(ConfirmDeletion);
    }
    /// <inheritdoc />
    public Task<bool> ConfirmOverwriteAsync(string key)
    {
        OverwriteConfirmations++;
        return Task.FromResult(ConfirmOverwrite);
    }
    /// <inheritdoc />
    public async Task ShowTemporaryLinkAsync(string key, Func<TimeSpan, Task<string>> generate)
    {
        if (GenerateLifetime is { } lifetime) await generate(lifetime);
    }
    /// <inheritdoc />
    public void CopyText(string text) => CopiedTexts.Add(text);
    /// <inheritdoc />
    public Task<ObjectStoragePreviewType?> RequestForcePreviewAsync(string name, long size, ObjectStoragePreviewType? inferredType) =>
        ForcePreviewHandler?.Invoke(name, size, inferredType) ?? Task.FromResult<ObjectStoragePreviewType?>(null);
}
