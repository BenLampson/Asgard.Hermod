namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>明确操作范围的共用确认窗体，初始焦点总是放在安全出口。</summary>
public partial class ObjectStorageConfirmationWindow : ObjectStorageDialogWindow
{
    /// <summary>删除与覆盖聚焦取消，纯提示聚焦唯一的关闭按钮。</summary>
    public ObjectStorageConfirmationWindow(ObjectStorageConfirmationDialogViewModel model)
    {
        InitializeComponent();
        BindModel(model);
        Loaded += (_, _) =>
        {
            if (model.RequiresConfirmation)
                CancelButton.Focus();
        };
    }
}
