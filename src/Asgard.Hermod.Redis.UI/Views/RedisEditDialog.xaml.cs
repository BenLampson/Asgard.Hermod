using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Views;

/// <summary>保存失败保持弹窗及草稿；标题栏关闭、Esc 与取消共用未保存确认。</summary>
public partial class RedisEditDialog : HermodDialog
{
    private readonly RedisKeyTabViewModel _model;
    private bool _allowClose;
    private bool _confirming;
    /// <summary>沿用当前键的完整基线、无损格式及安全提交命令。</summary>
    public RedisEditDialog(RedisKeyTabViewModel model)
    {
        _model = model; InitializeComponent(); DataContext = model;
        Loaded += (_, _) => Editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    private async void OnSave(object sender, RoutedEventArgs args)
    { if (await _model.SaveAsync()) { _allowClose = true; Close(); } }
    private void OnKeyDown(object sender, KeyEventArgs args)
    { if (args.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { args.Handled = true; OnSave(sender, args); } }
    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_confirming || _model.IsBusy) return;
        _confirming = true;
        try
        {
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            if (!await _model.RequestCloseAsync()) return;
            if (_model.IsEditing) _model.DiscardCommand.Execute(null);
            _allowClose = true; Close();
        }
        finally { _confirming = false; }
    }
}
