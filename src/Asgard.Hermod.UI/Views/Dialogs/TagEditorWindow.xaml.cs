using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>标签编辑窗体只处理焦点、快捷颜色面板和模态生命周期。</summary>
public partial class TagEditorWindow : HermodDialog
{
    private readonly TagEditorViewModel _model;

    /// <summary>使用统一窗体和操作栏显示由服务驱动的草稿。</summary>
    public TagEditorWindow(TagEditorViewModel model)
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

    /// <summary>补足未尝试的首次读取；失败保留重试入口，并将焦点放到名称。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_model.IsInitialized && _model.Error.Length == 0)
            await _model.InitializeAsync();
        if (IsVisible) NameInput.Focus();
    }

    /// <summary>折叠常用颜色只改变当前窗体布局，合法 HEX 始终可编辑。</summary>
    private void OnPaletteToggleClick(object sender, RoutedEventArgs args) =>
        ColorPalette.Visibility = ColorPalette.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>保存完成后将成功结果交给窗口宿主。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;

    /// <summary>提交进行中保留窗口，避免结果到达时窗口已被关闭。</summary>
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_model.IsBusy && _model.Result is null) args.Cancel = true;
    }

    /// <summary>清理宿主事件并停止未完成读取。</summary>
    private void OnClosed(object? sender, EventArgs args)
    {
        _model.CloseRequested -= OnCloseRequested;
        Loaded -= OnLoaded;
        Closing -= OnClosing;
        Closed -= OnClosed;
        _model.Dispose();
    }
}
