using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>删除标签确认窗体，展示全部影响后等待用户主动确认。</summary>
public partial class DeleteTagWindow : HermodDialog
{
    private readonly TagDeletionViewModel _model;

    /// <summary>注入确认状态，窗口不直接执行存储操作。</summary>
    public DeleteTagWindow(TagDeletionViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>宿主未尝试读取时补足预览；已有失败等待用户主动重试。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_model.Preview is null && _model.Error.Length == 0)
            await _model.InitializeAsync();
    }

    /// <summary>只有删除成功才返回确认结果。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;

    /// <summary>提交期间禁止关闭，成功后允许宿主结束模态窗口。</summary>
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_model.IsBusy && !_model.WasDeleted) args.Cancel = true;
    }

    /// <summary>结束窗体的全部事件与异步读取。</summary>
    private void OnClosed(object? sender, EventArgs args)
    {
        _model.CloseRequested -= OnCloseRequested;
        Loaded -= OnLoaded;
        Closing -= OnClosing;
        Closed -= OnClosed;
        _model.Dispose();
    }
}
