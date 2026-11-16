namespace Asgard.Hermod.ObjectStorage.UI.Views.Dialogs;

/// <summary>用同一个模态窗体确认删除并展示收集和批量删除进度。</summary>
public partial class ObjectStorageDeleteWindow : ObjectStorageDialogWindow
{
    private readonly ObjectStorageDeleteDialogViewModel _model;

    /// <summary>绑定已固定选择和回调的删除模型。</summary>
    public ObjectStorageDeleteWindow(ObjectStorageDeleteDialogViewModel model)
    {
        _model = model;
        InitializeComponent();
        BindModel(model);
    }

    /// <summary>执行中关闭等同停止，保留结果以说明是否可能已部分删除。</summary>
    protected override void OnClosing(CancelEventArgs args)
    {
        if (_model.IsRunning && !_model.IsWorkspaceClosing)
        {
            args.Cancel = true;
            _model.DismissCommand.Execute(null);
        }
        base.OnClosing(args);
    }
}
