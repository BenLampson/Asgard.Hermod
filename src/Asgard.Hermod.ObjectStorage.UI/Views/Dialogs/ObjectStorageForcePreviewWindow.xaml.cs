namespace Asgard.Hermod.ObjectStorage.UI.Views.Dialogs;

/// <summary>类型选择与费用确认共用 Hermod 模态容器，未知类型先聚焦选择器。</summary>
public partial class ObjectStorageForcePreviewWindow : ObjectStorageDialogWindow
{
    /// <summary>只有用户明确确认才返回类型，取消和标题栏关闭均不触发下载。</summary>
    public ObjectStorageForcePreviewWindow(ObjectStorageForcePreviewDialogViewModel model)
    {
        InitializeComponent();
        BindModel(model);
        Loaded += (_, _) =>
        {
            if (model.IsTypeInferred) CancelButton.Focus();
            else PreviewTypeSelector.Focus();
        };
    }
}
