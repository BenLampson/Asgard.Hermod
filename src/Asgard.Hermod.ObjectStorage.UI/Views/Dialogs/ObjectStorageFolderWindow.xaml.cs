namespace Asgard.Hermod.ObjectStorage.UI.Views.Dialogs;

/// <summary>新建文件夹窗体只负责共享表单焦点与模态返回。</summary>
public partial class ObjectStorageFolderWindow : ObjectStorageDialogWindow
{
    /// <summary>目录名称经过模型验证后才结束模态窗口。</summary>
    public ObjectStorageFolderWindow(ObjectStorageFolderDialogViewModel model)
    {
        InitializeComponent();
        BindModel(model);
        Loaded += (_, _) => NameInput.Focus();
    }
}
