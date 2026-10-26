namespace Asgard.Hermod.ObjectStorage.UI.Views.Dialogs;

/// <summary>共享模态容器中的目录属性，开始前警告，扫描中可取消，完成后显示属性。</summary>
public partial class ObjectStorageFolderPropertiesWindow : ObjectStorageDialogWindow
{
    /// <summary>窗口关闭时由基类释放模型并取消请求。</summary>
    public ObjectStorageFolderPropertiesWindow(ObjectStorageFolderPropertiesViewModel model)
    {
        InitializeComponent();
        BindModel(model);
    }
}
