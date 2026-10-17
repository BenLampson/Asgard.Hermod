namespace Asgard.Hermod.ObjectStorage.UI.Views.Dialogs;

/// <summary>临时链接窗体不在加载时生成或复制，所有操作由可见按钮触发。</summary>
public partial class ObjectStorageTemporaryLinkWindow : ObjectStorageDialogWindow
{
    /// <summary>初始焦点放在有效期选择，回车不会意外生成分享链接。</summary>
    public ObjectStorageTemporaryLinkWindow(ObjectStorageTemporaryLinkDialogViewModel model)
    {
        InitializeComponent();
        BindModel(model);
        Loaded += (_, _) => LifetimeSelector.Focus();
    }
}
