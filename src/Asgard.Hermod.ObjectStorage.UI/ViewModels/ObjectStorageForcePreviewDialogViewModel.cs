using Asgard.Hermod.ObjectStorage.UI.Models;

namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>仅收集图片类型和流量费用确认，不读取远程对象内容。</summary>
public sealed class ObjectStorageForcePreviewDialogViewModel : ObjectStorageDialogViewModel
{
    /// <summary>根据已经取得的对象信息设置默认类型；未知类型必须手动选择。</summary>
    public ObjectStorageForcePreviewDialogViewModel(string name, long size, ObjectStoragePreviewType? inferredType)
    {
        Name = name;
        Size = ObjectStoragePresentation.Size(size);
        IsTypeInferred = inferredType == ObjectStoragePreviewType.Image;
        Options = [new(ObjectStoragePreviewType.Image, Text.Get("ForcePreviewImage"))];
        ConfirmCommand = new RelayCommand(_ => { Result = SelectedType; Close(true); },
            _ => !IsDisposed && SelectedType == ObjectStoragePreviewType.Image);
        SelectedType = IsTypeInferred ? inferredType : null;
    }

    /// <summary>用户创建的对象名称保持原样。</summary>
    public string Name { get; }
    /// <summary>费用确认展示本次将读取的元数据大小。</summary>
    public string Size { get; }
    /// <summary>当前唯一支持的预览选项。</summary>
    public IReadOnlyList<ObjectStoragePreviewOption> Options { get; }
    /// <summary>只根据本地已有元数据推断，不发起嗅探下载。</summary>
    public bool IsTypeInferred { get; }
    /// <summary>无法推断时明确提示手动选择。</summary>
    public string TypeMessage => Text.Get(IsTypeInferred ? "ForcePreviewTypeInferred" : "ForcePreviewTypeUnknown");
    /// <summary>完整的费用说明句子允许翻译调整大小参数位置。</summary>
    public string TrafficWarning => Text.Format("ForcePreviewTrafficWarning", Size);
    /// <summary>选择无效或尚未选择时禁止确认；不会按翻译标签判断。</summary>
    public ObjectStoragePreviewType? SelectedType
    {
        get => field;
        set { if (SetProperty(ref field, value)) ConfirmCommand.NotifyCanExecuteChanged(); }
    }
    /// <summary>只有明确确认后的类型才会返回给下载调用方。</summary>
    public ObjectStoragePreviewType? Result { get; private set; }
    /// <summary>确认费用和类型后关闭弹窗，下载由工作区执行。</summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>关闭后禁止重复确认，不保留可操作的窗体状态。</summary>
    public override void Dispose()
    {
        base.Dispose();
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}
