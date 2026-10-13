using System.Text;

namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>读取并展示受限文本预览，当前选择变化后忽略旧对象的返回。</summary>
public sealed partial class ObjectStorageWorkspaceViewModel
{
    /// <summary>详情显示状态只存在于当前页签，不写入设置或影响所选对象。</summary>
    public bool IsDetailsOpen { get; private set; } = true;
    /// <summary>工具栏按钮明确说明下一次展开或收起行为。</summary>
    public string DetailsToggleLabel => Text.Get(IsDetailsOpen ? "HideObjectDetails" : "ShowObjectDetails");

    /// <summary>收起保留所选行并取消预览；重新展开时读取当前对象详情。</summary>
    public Task SetDetailsOpenAsync(bool open, CancellationToken cancellationToken = default)
    {
        if (IsClosed || IsDetailsOpen == open) return Task.CompletedTask;
        IsDetailsOpen = open;
        OnPropertyChanged(nameof(IsDetailsOpen)); OnPropertyChanged(nameof(DetailsToggleLabel));
        return LoadDetailsAsync(SelectedEntry, cancellationToken);
    }

    /// <summary>当前详情对象的名称。</summary>
    public string DetailName => SelectedEntry?.Name ?? "";
    /// <summary>详情类型图标。</summary>
    public string DetailIcon => SelectedEntry?.Icon ?? "file";
    /// <summary>详情与列表共享文件类别的主题色。</summary>
    public string DetailIconBrushKey => SelectedEntry?.IconBrushKey ?? "TextMutedBrush";
    /// <summary>没有选择时呈现对象详情的空态。</summary>
    public bool HasDetailSelection => SelectedEntry is not null;
    /// <summary>详情支持关闭后保留面板框架。</summary>
    public bool HasFileSelection => SelectedEntry is { IsDirectory: false };
    /// <summary>大小来自元数据或本页快照。</summary>
    public string DetailSize => SelectedEntry?.IsDirectory == true ? "—" : ObjectStoragePresentation.Size(Details?.Size ?? SelectedEntry?.Entry.Size ?? 0);
    /// <summary>更新时间来自服务元数据。</summary>
    public string DetailModified => ObjectStoragePresentation.Modified(Details?.LastModified ?? SelectedEntry?.Entry.LastModified);
    /// <summary>存储类型来自服务元数据。</summary>
    public string DetailStorageClass => ObjectStoragePresentation.StorageClass(Details?.StorageClass ?? SelectedEntry?.Entry.StorageClass ?? "");
    /// <summary>对象权限始终使用真实元数据，不假设公开访问。</summary>
    public string DetailAccess => Details?.AccessControl ?? "—";
    /// <summary>文件的 Content-Type。</summary>
    public string DetailContentType => Details?.ContentType ?? "—";
    /// <summary>预览不执行脚本、HTML 或 Markdown，只呈现只读文本。</summary>
    public string PreviewText => HasTextPreview ? Details!.PreviewText! : "";
    /// <summary>即使替换服务返回过大的字符串，也不将超出上限的文本放入控件。</summary>
    public bool HasTextPreview => !HasImagePreview && !IsImagePreviewLoading && _imagePreviewMessage.Length == 0
        && Details?.PreviewText is { } text && Encoding.UTF8.GetByteCount(text) <= 256 * 1024;
    /// <summary>没有文本预览时显示原因，不影响列表或下载。</summary>
    public string PreviewMessage => IsImagePreviewLoading ? Text.Get("ForcePreviewLoading") : _imagePreviewMessage.Length > 0 ? _imagePreviewMessage
        : IsDetailLoading ? Text.Get("ReadingObjectDetails") : DetailError.Length > 0 ? DetailError
        : SelectedEntry is null ? Text.Get("ClickAnObjectToViewItsDetails") : SelectedEntry.IsDirectory ? Text.Get("DoubleClickAFolderToOpenItsPrefix")
        : Details?.PreviewText is { } text && Encoding.UTF8.GetByteCount(text) > 256 * 1024 ? Text.Get("TheTextExceedsThe256KBPreviewLimitDownloadAndViewItLocally")
        : Details?.PreviewMessage ?? Text.Get("ThisFileCannotBePreviewedYetDownloadAndViewItLocallyListOperationsRemainAvailable");
    /// <summary>按扩展名提示预览格式，不选择浏览器执行器。</summary>
    public string PreviewFormat => Details?.ContentType == "application/json" ? "JSON" : "TEXT";

    /// <summary>显式选择入口使宿主和测试可等待详情完成。</summary>
    public async Task SelectEntryAsync(ObjectStorageEntryViewModel? entry, CancellationToken cancellationToken = default)
    {
        if (IsClosed) return;
        if (SetProperty(ref _selectedEntry, entry, nameof(SelectedEntry))) NotifyDetails();
        await LoadDetailsAsync(entry, cancellationToken);
    }

    /// <summary>双击目录进入前缀，双击文件下载后按打开规则处理。</summary>
    public Task OpenEntryAsync(ObjectStorageEntryViewModel entry, CancellationToken cancellationToken = default) =>
        entry.IsDirectory && SelectedBucket is { } bucket ? NavigateAsync(bucket, entry.Key, cancellationToken)
        : OpenFileCommand.ExecuteAsync(entry);

    /// <summary>每一次对象选择都有自己的取消令牌和版本号。</summary>
    private async Task LoadDetailsAsync(ObjectStorageEntryViewModel? entry, CancellationToken cancellationToken = default)
    {
        ResetImagePreview();
        _detailOperation?.Cancel();
        int version = ++_detailVersion;
        Details = null; DetailError = ""; IsDetailLoading = false;
        if (IsClosed || !IsDetailsOpen || entry is null || entry.IsDirectory || SelectedBucket is not { } bucket) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _detailOperation = operation;
        IsDetailLoading = true;
        try
        {
            var details = await _service.GetObjectDetailsAsync(_instance.Id, bucket, entry.Key, operation.Token);
            if (!IsClosed && !operation.IsCancellationRequested && version == _detailVersion && ReferenceEquals(SelectedEntry, entry))
            {
                entry.ApplyContentType(details.ContentType);
                Details = details;
                IsDetailLoading = false;
                await PreviewRecognizedImageAsync(entry, bucket, details, operation.Token);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!IsClosed && version == _detailVersion) DetailError = ObjectStoragePresentation.Error(exception, Text.Get("CannotReadObjectDetailsTryAgainOrDownloadToViewThem"));
        }
        finally
        {
            if (ReferenceEquals(_detailOperation, operation)) _detailOperation = null;
            if (!IsClosed && version == _detailVersion) IsDetailLoading = false;
        }
    }

    /// <summary>刷新所有元数据、预览和详情操作启用条件。</summary>
    private void NotifyDetails()
    {
        foreach (string property in new[] { nameof(DetailName), nameof(DetailIcon), nameof(DetailIconBrushKey), nameof(HasDetailSelection), nameof(HasFileSelection),
            nameof(DetailSize), nameof(DetailModified), nameof(DetailStorageClass), nameof(DetailAccess), nameof(DetailContentType),
            nameof(PreviewText), nameof(HasTextPreview), nameof(PreviewMessage), nameof(PreviewFormat),
            nameof(ShowForcePreviewActions), nameof(ShowForcePreviewIntroduction), nameof(ForcePreviewLabel) }) OnPropertyChanged(property);
        RefreshCommands();
    }
}
