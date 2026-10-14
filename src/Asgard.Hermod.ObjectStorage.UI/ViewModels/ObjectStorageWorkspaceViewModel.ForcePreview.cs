using System.IO;
using Asgard.Hermod.ObjectStorage.UI.Models;

namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>点击预览后已识别图片直接读取，未知类型确认后读取；选择变化时隔离旧对象的返回。</summary>
public sealed partial class ObjectStorageWorkspaceViewModel
{
    private CancellationTokenSource? _imagePreviewOperation;
    private Task _imagePreviewTask = Task.CompletedTask;
    private int _imagePreviewVersion;
    private string _imagePreviewMessage = "";
    private long _imagePreviewBytes;
    private long _imagePreviewSize;
    private IManagedTemporaryFile? _imagePreviewFile;

    /// <summary>用户点击后，已识别图片直接读取，未知类型先确认类型与费用。</summary>
    public AsyncRelayCommand ForcePreviewCommand { get; private set; } = null!;
    /// <summary>取消本次图片读取，不影响其他传输任务。</summary>
    public RelayCommand CancelImagePreviewCommand { get; private set; } = null!;
    /// <summary>用本机打开方式查看已下载图片，不创建内置图片窗口或重复下载。</summary>
    public AsyncRelayCommand OpenImagePreviewCommand { get; private set; } = null!;
    /// <summary>已经安全解码并冻结的缩略图，源文件仅由当前预览租约管理。</summary>
    public ImageSource? ImagePreview
    {
        get => field;
        private set { if (SetProperty(ref field, value)) NotifyImagePreview(); }
    }
    /// <summary>用于图片与文字预览区域的互斥显示。</summary>
    public bool HasImagePreview => ImagePreview is not null;
    /// <summary>只在实际读取及解码期间显示加载状态。</summary>
    public bool IsImagePreviewLoading
    {
        get => field;
        private set { if (SetProperty(ref field, value)) NotifyImagePreview(); }
    }
    /// <summary>未加载图片时，所有普通文件均可手动尝试图片类型。</summary>
    public bool ShowForcePreviewActions => HasFileSelection && !HasImagePreview && !IsImagePreviewLoading;
    /// <summary>只在未知类型且没有文本预览时说明手动类型选择。</summary>
    public bool ShowForcePreviewIntroduction => ShowForcePreviewActions && !IsRecognizedPreviewImage && !HasTextPreview && _imagePreviewMessage.Length == 0;
    /// <summary>已识别图片只需重新加载，未知类型才需再次选择。</summary>
    public string ForcePreviewLabel => Text.Get(_imagePreviewMessage.Length == 0 ? "ForcePreview"
        : IsRecognizedPreviewImage ? "ForcePreviewRetryImage" : "ForcePreviewChooseTypeAgain");
    /// <summary>显示实际读取进度，未知总大小时不编造百分比。</summary>
    public string ImagePreviewProgress => _imagePreviewSize > 0
        ? Text.Format("ForcePreviewProgress", ObjectStoragePresentation.Size(_imagePreviewBytes), ObjectStoragePresentation.Size(_imagePreviewSize))
        : Text.Format("ForcePreviewProgressUnknown", ObjectStoragePresentation.Size(_imagePreviewBytes));

    /// <summary>初始化命令后参与工作区统一启用条件刷新。</summary>
    private void InitializeForcePreviewCommands()
    {
        ForcePreviewCommand = new AsyncRelayCommand(_ => ConfirmForcePreviewAsync(), _ =>
            CanOperate && IsDetailsOpen && _temporaryFiles is not null && HasFileSelection && !IsDetailLoading && !IsImagePreviewLoading && !HasImagePreview);
        CancelImagePreviewCommand = new RelayCommand(_ => { _imagePreviewOperation?.Cancel(); CancelImagePreviewCommand.NotifyCanExecuteChanged(); },
            _ => !IsClosed && IsImagePreviewLoading && _imagePreviewOperation?.IsCancellationRequested == false);
        OpenImagePreviewCommand = new AsyncRelayCommand(_ => InteractAsync(() => TrackImagePreviewTask(OpenCachedImageAsync(_lifetime.Token))),
            _ => CanOperate && _files is not null && HasImagePreview && _imagePreviewFile is not null);
    }

    /// <summary>只使用已经取得的信息推断；扩展名与内容类型均不触发额外流量。</summary>
    private static ObjectStoragePreviewType? InferPreviewType(string name, string? contentType)
    {
        if (FilePreviewPolicy.IsImage(name)) return ObjectStoragePreviewType.Image;
        string? mediaType = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        return mediaType is "image/png" or "image/jpeg" or "image/bmp" or "image/x-ms-bmp" or "image/gif"
            or "image/tiff" or "image/x-icon" or "image/vnd.microsoft.icon" ? ObjectStoragePreviewType.Image : null;
    }

    /// <summary>识别只依据文件名与已经读取的元数据，不为识别类型嗅探远程内容。</summary>
    private bool IsRecognizedPreviewImage => SelectedEntry is { IsDirectory: false } entry
        && InferPreviewType(entry.Name, Details?.ContentType) == ObjectStoragePreviewType.Image;

    /// <summary>每次读取使用独立版本，并跟踪仍在结束的旧下载，页签关闭等待全部缓存释放。</summary>
    private Task StartImagePreviewAsync(ObjectStorageEntryViewModel entry, ObjectStorageBucket bucket, long size,
        CancellationToken cancellationToken = default)
    {
        Task read = ReadImagePreviewAsync(entry, bucket, size, ++_imagePreviewVersion, cancellationToken);
        return TrackImagePreviewTask(read);
    }

    /// <summary>页签关闭等待下载与外部打开完成，避免提前释放临时存储。</summary>
    private Task TrackImagePreviewTask(Task operation)
    {
        Task completion = WaitForImageOperationAsync(operation);
        _imagePreviewTask = _imagePreviewTask.IsCompleted ? completion : Task.WhenAll(_imagePreviewTask, completion);
        return operation;
    }

    /// <summary>错误由交互入口展示；关闭页签只等待租约释放，不重复抛出已经处理的启动错误。</summary>
    private static async Task WaitForImageOperationAsync(Task operation) => await operation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    /// <summary>打开期间独占租约；成功后保护文件供看图软件读取，失败仍可重试。</summary>
    private async Task OpenCachedImageAsync(CancellationToken cancellationToken)
    {
        if (_imagePreviewFile is not { } file || _files is null || SelectedEntry is not { } entry || SelectedBucket is not { } bucket) return;
        int version = _imagePreviewVersion;
        _imagePreviewFile = null;
        try
        {
            await _files.OpenAsync(file.Path, cancellationToken);
            file.RetainForExternalUse();
        }
        finally
        {
            if (IsCurrentImagePreview(entry, bucket, version)) _imagePreviewFile = file;
            else file.Dispose();
        }
    }

    /// <summary>确认时捕获对象及版本；确认返回后对象已改变就忽略结果，不下载原对象。</summary>
    private async Task ConfirmForcePreviewAsync()
    {
        if (SelectedEntry is not { IsDirectory: false } entry || SelectedBucket is not { } bucket || _temporaryFiles is null) return;
        int version = _imagePreviewVersion;
        long size = Details?.Size ?? entry.Entry.Size;
        if (IsRecognizedPreviewImage)
        {
            await StartImagePreviewAsync(entry, bucket, size);
            return;
        }
        ObjectStoragePreviewType? selected = null;
        await InteractAsync(async () => selected = await _dialogs.RequestForcePreviewAsync(entry.Name, size,
            null));
        if (selected != ObjectStoragePreviewType.Image || !IsCurrentImagePreview(entry, bucket, version)) return;
        await StartImagePreviewAsync(entry, bucket, size);
    }

    /// <summary>按实际字节限流并在后台解码；成功时保留受管文件，过期、失败与取消立即释放。</summary>
    private async Task ReadImagePreviewAsync(ObjectStorageEntryViewModel entry, ObjectStorageBucket bucket, long size, int version,
        CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _imagePreviewOperation = operation;
        _imagePreviewMessage = "";
        _imagePreviewBytes = 0; _imagePreviewSize = size;
        ImagePreview = null;
        IsImagePreviewLoading = true;
        IManagedTemporaryFile? file = null;
        try
        {
            if (size > FilePreviewPolicy.ImageLimit) throw Text.Exception("FilePreviewTooLarge");
            file = _temporaryFiles!.CreateFile(entry.Name);
            var progress = new Progress<ObjectStorageTransferProgress>(value =>
            {
                if (IsCurrentImagePreview(entry, bucket, version) && !operation.IsCancellationRequested && IsImagePreviewLoading)
                {
                    _imagePreviewBytes = value.TransferredBytes;
                    _imagePreviewSize = value.TotalBytes;
                    OnPropertyChanged(nameof(ImagePreviewProgress));
                }
            });
            await _service.DownloadAsync(_instance.Id, bucket, entry.Key, file.Path, progress, operation.Token, FilePreviewPolicy.ImageLimit);
            operation.Token.ThrowIfCancellationRequested();
            var content = await FilePreviewLoader.LoadImageAsync(file.Path, operation.Token);
            if (IsCurrentImagePreview(entry, bucket, version) && !operation.IsCancellationRequested)
            {
                file = NormalizeImageExtension(file, content.ImageExtension!);
                _imagePreviewFile = file; file = null;
                ImagePreview = content.Image;
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (IsCurrentImagePreview(entry, bucket, version)) _imagePreviewMessage = Text.Get("ForcePreviewCancelled");
        }
        catch (Exception exception)
        {
            if (IsCurrentImagePreview(entry, bucket, version))
                _imagePreviewMessage = operation.IsCancellationRequested ? Text.Get("ForcePreviewCancelled")
                    : Text.Format("ForcePreviewFailureWithTraffic", ObjectStoragePresentation.Error(exception, Text.Get("ForcePreviewFailed")));
        }
        finally
        {
            file?.Dispose();
            if (ReferenceEquals(_imagePreviewOperation, operation)) _imagePreviewOperation = null;
            if (IsCurrentImagePreview(entry, bucket, version))
            {
                IsImagePreviewLoading = false;
                NotifyImagePreview();
            }
        }
    }

    /// <summary>未知扩展名仅在本机缓存中改为实际图片格式；原始正文与远程对象均保持不变。</summary>
    private IManagedTemporaryFile NormalizeImageExtension(IManagedTemporaryFile file, string extension)
    {
        if (FilePreviewPolicy.IsImage(file.Path)) return file;
        var renamed = _temporaryFiles!.CreateFile(Path.GetFileNameWithoutExtension(file.Path) + extension);
        try { File.Move(file.Path, renamed.Path); file.Dispose(); return renamed; }
        catch { renamed.Dispose(); throw; }
    }

    /// <summary>版本、对象引用与容器同时匹配才允许更新当前详情。</summary>
    private bool IsCurrentImagePreview(ObjectStorageEntryViewModel entry, ObjectStorageBucket bucket, int version) =>
        !IsClosed && version == _imagePreviewVersion && ReferenceEquals(SelectedEntry, entry) && SelectedBucket == bucket;

    /// <summary>切换、刷新与关闭详情立即清除图片并使旧回调失效。</summary>
    private void ResetImagePreview()
    {
        ++_imagePreviewVersion;
        _imagePreviewOperation?.Cancel();
        _imagePreviewFile?.Dispose(); _imagePreviewFile = null;
        _imagePreviewMessage = ""; _imagePreviewBytes = 0; _imagePreviewSize = 0;
        ImagePreview = null;
        IsImagePreviewLoading = false;
        NotifyImagePreview();
    }

    /// <summary>让互斥预览、进度、重试标签及命令同步更新。</summary>
    private void NotifyImagePreview()
    {
        foreach (string property in new[] { nameof(HasImagePreview), nameof(ShowForcePreviewActions), nameof(ShowForcePreviewIntroduction),
            nameof(ForcePreviewLabel), nameof(ImagePreviewProgress) }) OnPropertyChanged(property);
        NotifyDetails();
    }
}
