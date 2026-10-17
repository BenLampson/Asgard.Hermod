using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>主窗体关闭选择；取消、Escape 和标题栏关闭均没有选择或持久化副作用。</summary>
public sealed class MainWindowCloseDialog : HermodDialog
{
    private readonly Button _hide;
    private readonly TextBlock _unavailable;
    private readonly CheckBox _remember;
    /// <summary>沿用共享窗口、主题和动作栏，默认焦点为取消。</summary>
    public MainWindowCloseDialog(bool canHide)
    {
        Title = Text.Get("CloseMainWindow"); Width = 580; MinHeight = 0; Height = double.NaN;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = Text.Get("MainCloseQuestion"), FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = Text.Get("MainCloseDescription"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20) });
        _remember = new CheckBox { Content = Text.Get("MainCloseDoNotAsk") }; body.Children.Add(_remember);
        _unavailable = new TextBlock { Text = Text.Get("MainCloseTrayUnavailable"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) };
        _unavailable.SetResourceReference(ForegroundProperty, "WarningBrush"); body.Children.Add(_unavailable);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var cancel = new Button { Content = Text.Get("Cancel"), IsCancel = true, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        _hide = new Button { Content = Text.Get("HideMainWindowToTray"), Margin = new Thickness(0, 0, 10, 0) };
        _hide.Click += (_, _) => Choose(MainWindowCloseAction.HideToTray);
        var exit = new Button { Content = Text.Get("ExitHermod") }; exit.SetResourceReference(StyleProperty, "DangerButton");
        exit.Click += (_, _) => Choose(MainWindowCloseAction.ExitApplication);
        FooterContent = new DialogActionBar { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, _hide, exit } } };
        SetTrayAvailable(canHide); Loaded += (_, _) => cancel.Focus();
    }
    /// <summary>只有明确选择动作才有结果。</summary>
    public MainWindowCloseAction? SelectedAction { get; private set; }
    /// <summary>由主窗体在确认结果之后保存，取消时不会保存。</summary>
    public bool DoNotAskAgain => _remember.IsChecked == true;
    /// <summary>Explorer 或通知区域失效时立即禁止隐藏，说明保留在当前对话框。</summary>
    public void SetTrayAvailable(bool available)
    {
        _hide.IsEnabled = available; _unavailable.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Choose(MainWindowCloseAction action) { SelectedAction = action; DialogResult = true; }
}
