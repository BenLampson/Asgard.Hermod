using System.IO;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>所有对象操作捕获明确的 Bucket 与键，通过替换式交互服务取得用户选择。</summary>
public sealed partial class ObjectStorageWorkspaceViewModel
{
    /// <summary>只在编辑保存成功后失效当前连接和容器缓存。</summary>
    private async Task EditConnectionAsync()
    {
        if (_editor is null) return;
        await InteractAsync(async () =>
        {
            var summary = await _editor.EditAsync(_instance.Id);
            if (summary is null || IsClosed) return;
            _instance.UpdateSnapshot(summary);
            Header = _instance.DisplayName;
            OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Tags));
            await RefreshBucketsAsync(_lifetime.Token);
        });
    }

    /// <summary>新建文件夹名称由窗体验证，重名和权限失败由真实服务报告。</summary>
    private async Task CreateFolderAsync()
    {
        if (SelectedBucket is not { } bucket) return;
        string prefix = Prefix;
        await InteractAsync(async () =>
        {
            string? name = await _dialogs.RequestFolderNameAsync(bucket, prefix);
            if (name is null || IsClosed) return;
            await _service.CreateFolderAsync(_instance.Id, bucket, prefix, name, _lifetime.Token);
            if (!IsClosed && SelectedBucket == bucket && Prefix == prefix) await RefreshAsync(_lifetime.Token);
        });
    }

    /// <summary>开始上传时先确认队列，任务独立运行而不阻塞目录浏览。</summary>
    private async Task SelectUploadAsync()
    {
        if (SelectedBucket is not { } bucket) return;
        string prefix = Prefix;
        await InteractAsync(async () =>
        {
            var selection = await _dialogs.SelectUploadAsync(bucket, prefix);
            if (selection is null || IsClosed) return;
            foreach (var item in selection.Items)
            {
                if (!item.Key.StartsWith(prefix, StringComparison.Ordinal) || item.Key.Length == prefix.Length || item.Key.EndsWith('/'))
                    throw new ObjectStorageException("上传目标不在已选择的目录中，请重新选择文件。");
                QueueTransfer(new(item.Key, true, item.Size, async (task, progress, token) =>
                {
                    await _transferGate.WaitAsync(token);
                    try
                    {
                        task.MarkTransferring();
                        bool exists = selection.OverwritePolicy != ObjectStorageOverwritePolicy.Overwrite
                            && await _service.ObjectExistsAsync(_instance.Id, bucket, item.Key, token);
                        bool overwrite = selection.OverwritePolicy == ObjectStorageOverwritePolicy.Overwrite;
                        if (exists && selection.OverwritePolicy == ObjectStorageOverwritePolicy.Skip) { task.MarkSkipped(); return; }
                        if (exists && selection.OverwritePolicy == ObjectStorageOverwritePolicy.Ask)
                        {
                            overwrite = await _dialogs.ConfirmOverwriteAsync(item.Key);
                            token.ThrowIfCancellationRequested();
                            if (!overwrite) { task.MarkSkipped(); return; }
                        }
                        try { await _service.UploadAsync(_instance.Id, bucket, item.Key, item.LocalPath, overwrite, progress, token); }
                        catch (ObjectStorageException conflict) when (!overwrite && conflict.ErrorCode == "ObjectAlreadyExists")
                        {
                            if (selection.OverwritePolicy == ObjectStorageOverwritePolicy.Skip) { task.MarkSkipped(); return; }
                            if (!await _dialogs.ConfirmOverwriteAsync(item.Key)) { task.MarkSkipped(); return; }
                            token.ThrowIfCancellationRequested();
                            await _service.UploadAsync(_instance.Id, bucket, item.Key, item.LocalPath, true, progress, token);
                        }
                    }
                    finally { _transferGate.Release(); }
                }, _lifetime.Token), bucket, prefix);
            }
        });
    }

    /// <summary>下载文件先取得用户选择的完整路径；目录按明确的相对结构批量下载。</summary>
    private async Task DownloadAsync(object? parameter)
    {
        if (Target(parameter) is not { } entry || SelectedBucket is not { } bucket) return;
        string originPrefix = Prefix;
        await InteractAsync(async () =>
        {
            if (entry.IsDirectory)
            {
                string? folder = await _dialogs.SelectDownloadFolderAsync();
                if (folder is not null && !IsClosed) await QueueDownloadsAsync(bucket, [entry.Entry], folder, originPrefix);
                return;
            }
            string? path = await _dialogs.SelectDownloadPathAsync(entry.Name);
            if (path is not null && !IsClosed) QueueDownload(bucket, entry.Entry, path, originPrefix);
        });
    }

    /// <summary>批量下载只处理用户勾选范围，文件夹按目录递归展开。</summary>
    private async Task DownloadCheckedAsync()
    {
        if (SelectedBucket is not { } bucket) return;
        string originPrefix = Prefix;
        var entries = Entries.Where(entry => entry.IsChecked).Select(entry => entry.Entry).ToArray();
        await InteractAsync(async () =>
        {
            string? folder = await _dialogs.SelectDownloadFolderAsync();
            if (folder is not null && !IsClosed) await QueueDownloadsAsync(bucket, entries, folder, originPrefix);
        });
    }

    /// <summary>安全映射完整对象键，拒绝路径穿越和 Windows 名称冲突。</summary>
    private async Task QueueDownloadsAsync(ObjectStorageBucket bucket, IReadOnlyList<ObjectStorageEntry> selected, string folder, string originPrefix)
    {
        var files = new Dictionary<string, ObjectStorageEntry>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        async Task Expand(ObjectStorageEntry entry)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            if (!entry.IsDirectory) { files.TryAdd(entry.Key, entry); return; }
            if (!visited.Add(entry.Key)) return;
            string? continuation = null;
            do
            {
                var page = await _service.ListObjectsAsync(_instance.Id, bucket, entry.Key, continuationToken: continuation, cancellationToken: _lifetime.Token);
                foreach (var child in page.Entries) await Expand(child);
                continuation = page.NextToken;
            } while (continuation is not null);
        }
        foreach (var entry in selected) await Expand(entry);
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downloads = new List<(ObjectStorageEntry Entry, string Path)>();
        foreach (var entry in files.Values)
        {
            string relative = entry.Key.StartsWith(originPrefix, StringComparison.Ordinal) ? entry.Key[originPrefix.Length..] : entry.Key;
            var parts = relative.Split('/');
            if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || part.EndsWith(' ') || part.EndsWith('.') || IsReservedName(part)))
                throw new ObjectStorageException("所选对象键无法安全保存为本地文件名，请逐个选择下载位置。");
            string path = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                throw new ObjectStorageException("所选对象映射到重复或无效的本地路径，请逐个下载。");
            downloads.Add((entry, path));
        }
        if (IsClosed) return;
        foreach (var download in downloads)
        {
            if (File.Exists(download.Path) && !await _dialogs.ConfirmOverwriteAsync(download.Entry.Key)) continue;
            if (IsClosed) return;
            QueueDownload(bucket, download.Entry, download.Path, originPrefix);
        }
    }

    /// <summary>禁止写入 Windows 的保留设备名称。</summary>
    private static bool IsReservedName(string name)
    {
        string stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
            || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9';
    }

    /// <summary>下载任务捕获本地路径，不使用随后变化的页面前缀。</summary>
    private void QueueDownload(ObjectStorageBucket bucket, ObjectStorageEntry entry, string path, string originPrefix) =>
        QueueTransfer(new(entry.Key, false, entry.Size, async (task, progress, token) =>
        {
            await _transferGate.WaitAsync(token);
            try { task.MarkTransferring(); await _service.DownloadAsync(_instance.Id, bucket, entry.Key, path, progress, token); }
            finally { _transferGate.Release(); }
        }, _lifetime.Token), bucket, originPrefix);

    /// <summary>只有完整键清单已经展示并确认后才发出删除请求。</summary>
    private async Task DeleteAsync(object? parameter)
    {
        if (SelectedBucket is not { } bucket) return;
        string prefix = Prefix;
        var entries = parameter is ObjectStorageEntryViewModel row ? new[] { row.Entry }
            : HasCheckedEntries ? Entries.Where(entry => entry.IsChecked).Select(entry => entry.Entry).ToArray()
            : SelectedEntry is { } selected ? new[] { selected.Entry } : [];
        if (entries.Length == 0) return;
        await InteractAsync(async () =>
        {
            var plan = await _service.PrepareDeletionAsync(_instance.Id, bucket, entries, _lifetime.Token);
            if (IsClosed || plan.Keys.Count == 0) return;
            if (!await _dialogs.ConfirmDeleteAsync(bucket, prefix, plan) || IsClosed) return;
            try { await _service.DeleteAsync(_instance.Id, bucket, plan, _lifetime.Token); }
            finally { if (!IsClosed && SelectedBucket == bucket && Prefix == prefix) await RefreshAsync(_lifetime.Token); }
        });
    }

    /// <summary>生成回调仅由窗口内明确的生成操作调用。</summary>
    private async Task ShowTemporaryLinkAsync(object? parameter)
    {
        if (Target(parameter) is not { IsDirectory: false } entry || SelectedBucket is not { } bucket) return;
        await InteractAsync(() => _dialogs.ShowTemporaryLinkAsync(entry.Key, lifetime =>
            _service.CreateTemporaryLinkAsync(_instance.Id, bucket, entry.Key, lifetime, _lifetime.Token)));
    }

    /// <summary>剪贴板写入必须来自明确的用户命令。</summary>
    private void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text) || IsClosed) return;
        try { _dialogs.CopyText(text); }
        catch (Exception exception) { ActionError = ObjectStoragePresentation.Error(exception, "无法写入剪贴板，请重试。"); }
    }

    /// <summary>准备失败保持已加载列表，关闭后忽略交互返回。</summary>
    private async Task InteractAsync(Func<Task> action)
    {
        _dialogOpen = true; ActionError = ""; RefreshCommands();
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { if (!IsClosed) ActionError = ObjectStoragePresentation.Error(exception, "操作失败，请检查网络和访问权限后重试。"); }
        finally { _dialogOpen = false; RefreshCommands(); }
    }

    /// <summary>新增任务立即展开，任务执行不占用列表命令。</summary>
    private void QueueTransfer(ObjectStorageTransferViewModel transfer, ObjectStorageBucket bucket, string prefix)
    {
        transfer.Finished += OnTransferFinished;
        transfer.PropertyChanged += (_, args) =>
        {
            if (!IsClosed && args.PropertyName is nameof(transfer.IsRunning) or nameof(transfer.IsFailed))
            {
                OnPropertyChanged(nameof(TransferSummary)); OnPropertyChanged(nameof(EditConnectionHint));
                EditConnectionCommand.NotifyCanExecuteChanged();
            }
        };
        transfer.Finished += async (_, _) =>
        {
            if (!IsClosed && transfer.IsUpload && transfer.IsCompleted && SelectedBucket == bucket && Prefix == prefix)
                await RefreshAsync(_lifetime.Token);
        };
        Transfers.Add(transfer); IsTransferExpanded = true;
        _ = transfer.RunAsync();
    }

    /// <summary>任务结束同步折叠栏统计，失败任务可在原位置重试。</summary>
    private void OnTransferFinished(object? sender, EventArgs args) => OnPropertyChanged(nameof(TransferSummary));
}
