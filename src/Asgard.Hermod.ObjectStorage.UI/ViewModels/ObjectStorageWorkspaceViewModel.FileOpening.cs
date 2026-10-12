namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>远程文件完整下载后才能打开，取消和关闭页面不会启动外部程序。</summary>
public sealed partial class ObjectStorageWorkspaceViewModel
{
    /// <summary>下载完成后按本机规则或系统默认关联打开。</summary>
    public AsyncRelayCommand OpenFileCommand { get; }
    /// <summary>图片和文本以只读方式在 Hermod 内预览。</summary>
    public AsyncRelayCommand PreviewFileCommand { get; }

    /// <summary>捕获当前文件与 Bucket，重试创建新的隔离缓存目录。</summary>
    private Task QueueFileOpenAsync(object? parameter, bool preview)
    {
        if (_files is null || _temporaryFiles is null || Target(parameter) is not { IsDirectory: false } entry || SelectedBucket is not { } bucket)
            return Task.CompletedTask;
        return InteractAsync(() =>
        {
            long? limit = preview ? FilePreviewPolicy.Limit(entry.Name) : null;
            if (preview && (limit == 0 || entry.Entry.Size > limit))
                throw Text.Exception(limit == 0 ? "FilePreviewUnsupported" : "FilePreviewTooLarge");
            QueueTransfer(new(entry.Key, false, entry.Entry.Size, async (task, progress, token) =>
            {
                IManagedTemporaryFile? file = null;
                try
                {
                    await _transferGate.WaitAsync(token);
                    try
                    {
                        file = _temporaryFiles.CreateFile(entry.Name);
                        task.MarkTransferring();
                        await _service.DownloadAsync(_instance.Id, bucket, entry.Key, file.Path, progress, token, limit);
                    }
                    finally { _transferGate.Release(); }
                    token.ThrowIfCancellationRequested();
                    if (IsClosed) return;
                    if (preview) await _files.PreviewAsync(file.Path, token);
                    else { await _files.OpenAsync(file.Path, token); file.RetainForExternalUse(); }
                }
                finally { file?.Dispose(); }
            }, _lifetime.Token), bucket, Prefix);
            return Task.CompletedTask;
        });
    }
}
