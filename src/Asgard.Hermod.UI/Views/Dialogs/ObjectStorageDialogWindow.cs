using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>对象操作窗体共用模态关闭、取消后台工作与事件释放。</summary>
public class ObjectStorageDialogWindow : HermodDialog
{
    private ObjectStorageDialogViewModel? _model;

    /// <summary>派生窗体加载 XAML 后只绑定一次业务模型。</summary>
    protected void BindModel(ObjectStorageDialogViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
    }

    /// <summary>标题栏关闭也终止异步枚举或忽略迟到链接。</summary>
    protected override void OnClosing(CancelEventArgs args)
    {
        base.OnClosing(args);
        if (!args.Cancel)
            _model?.Dispose();
    }

    /// <summary>模态循环结束后释放业务事件，不留下已关闭窗体引用。</summary>
    protected override void OnClosed(EventArgs args)
    {
        if (_model is not null)
        {
            _model.CloseRequested -= OnCloseRequested;
            _model.Dispose();
        }
        base.OnClosed(args);
    }

    /// <summary>显式确认与取消都转换为宿主可检查的模态结果。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;
}
