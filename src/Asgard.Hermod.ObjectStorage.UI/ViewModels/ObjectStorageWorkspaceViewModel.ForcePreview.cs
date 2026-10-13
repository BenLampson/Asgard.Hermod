using Asgard.Hermod.ObjectStorage.UI.Models;

namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>显式确认费用后加载右侧图片预览；选择变化时取消并隔离旧对象的返回。</summary>
public sealed partial class ObjectStorageWorkspaceViewModel
{
    private CancellationTokenSource? _imagePreviewOperation;
    private Task _imagePreviewTask = Task.CompletedTask;
    private int _imagePreviewVersion;
    private string _imagePreviewMessage = "";
    private long _imagePreviewBytes;
    private long _imagePreviewSize;

    /// <summary>点击入口仅确认类型和费用，确认之后才读取内容。</summary>
    public AsyncRelayCommand ForcePreviewCommand { get; private set; } = null!;
    /// <summary>取消本次图片读取，不影响其他传输任务。</summary>
    public RelayCommand CancelImagePreviewCommand { get; private set; } = null!;
    /// <summary>已经安全解码并冻结的图片，不保存源文件或远程签名链接。</summary>
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
    /// <summary>有文本预览时不重复展示无法预览说明。</summary>
    public bool ShowForcePreviewIntroduction => ShowForcePreviewActions && !HasTextPreview && _imagePreviewMessage.Length == 0;
    /// <summary>失败或取消后使用同一个入口重新确认类型及费用。</summary>
    public string ForcePreviewLabel => Text.Get(_imagePreviewMessage.Length == 0 ? "ForcePreview" : "ForcePreviewChooseTypeAgain");
    /// <summary>显示实际读取进度，未知总大小时不编造百分比。</summary>
    public string ImagePreviewProgress => _imagePreviewSize > 0
        ? Text.Format("ForcePreviewProgress", ObjectStoragePresentation.Size(_imagePreviewBytes), ObjectStoragePresentation.Size(_imagePreviewSize))
        : Text.Format("ForcePreviewProgressUnknown", ObjectStoragePresentation.Size(_imagePreviewBytes));

    /// <summary>初始化命令后参与工作区统一启用条件刷新。</summary>
    private void InitializeForcePreviewCommands()
    {
        ForcePreviewCommand = new AsyncRelayCommand(_ => ConfirmForcePreviewAsync(), _ =>
            CanOperate && _temporaryFiles is not null && HasFileSelection && !IsDetailLoading && !IsImagePreviewLoading);
        CancelImagePreviewCommand = new RelayCommand(_ => { _imagePreviewOperation?.Cancel(); CancelImagePreviewCommand.NotifyCanExecuteChanged(); },
            _ => !IsClosed && IsImagePreviewLoading && _imagePreviewOperation?.IsCancellationRequested == false);
    }

    /// <summary>只使用已经取得的信息推断；扩展名与内容类型均不触发额外流量。</summary>
    private static ObjectStoragePreviewType? InferPreviewType(string name, string? contentType)
    {
        if (FilePreviewPolicy.IsImage(name)) return ObjectStoragePreviewType.Image;
        string? mediaType = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        return mediaType is "image/png" or "image/jpeg" or "image/bmp" or "image/x-ms-bmp" or "image/gif"
            or "image/tiff" or "image/x-icon" or "image/vnd.microsoft.icon" ? ObjectStoragePreviewType.Image : null;
    }

    /// <summary>确认时捕获对象及版本；确认返回后对象已改变就忽略结果，不下载原对象。</summary>
    private async Task ConfirmForcePreviewAsync()
    {
        if (SelectedEntry is not { IsDirectory: false } entry || SelectedBucket is not { } bucket || _temporaryFiles is null) return;
        int version = _imagePreviewVersion;
        long size = Details?.Size ?? entry.Entry.Size;
        ObjectStoragePreviewType? selected = null;
        await InteractAsync(async () => selected = await _dialogs.RequestForcePreviewAsync(entry.Name, size,
            InferPreviewType(entry.Name, Details?.ContentType)));
        if (selected != ObjectStoragePreviewType.Image || !IsCurrentImagePreview(entry, bucket, version)) return;
        _imagePreviewTask = ReadImagePreviewAsync(entry, bucket, size, ++_imagePreviewVersion);
        await _imagePreviewTask;
    }

    /// <summary>下载到受管临时文件，按实际字节限流并在后台解码，任何退出路径均释放缓存。</summary>
    private async Task ReadImagePreviewAsync(ObjectStorageEntryViewModel entry, ObjectStorageBucket bucket, long size, int version)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _imagePreviewOperation = operation;
        _imagePreviewMessage = "";
        _imagePreviewBytes = 0; _imagePreviewSize = size;
        ImagePreview = null;
        IsImagePreviewLoading = true;
        try
        {
            if (size > FilePreviewPolicy.ImageLimit) throw Text.Exception("FilePreviewTooLarge");
            using var file = _temporaryFiles!.CreateFile(entry.Name);
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
            if (IsCurrentImagePreview(entry, bucket, version) && !operation.IsCancellationRequested) ImagePreview = content.Image;
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
            if (ReferenceEquals(_imagePreviewOperation, operation)) _imagePreviewOperation = null;
            if (IsCurrentImagePreview(entry, bucket, version))
            {
                IsImagePreviewLoading = false;
                NotifyImagePreview();
            }
        }
    }

    /// <summary>版本、对象引用与容器同时匹配才允许更新当前详情。</summary>
    private bool IsCurrentImagePreview(ObjectStorageEntryViewModel entry, ObjectStorageBucket bucket, int version) =>
        !IsClosed && version == _imagePreviewVersion && ReferenceEquals(SelectedEntry, entry) && SelectedBucket == bucket;

    /// <summary>切换、刷新与关闭详情立即清除图片并使旧回调失效。</summary>
    private void ResetImagePreview()
    {
        ++_imagePreviewVersion;
        _imagePreviewOperation?.Cancel();
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
