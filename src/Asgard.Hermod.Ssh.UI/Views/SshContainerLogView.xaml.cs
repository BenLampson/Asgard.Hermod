using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>独立日志阅读器的本地搜索、选区和导出；不发送终端输入。</summary>
public partial class SshContainerLogView : UserControl, IDisposable
{
    private SshContainerLogViewModel? _model;
    private readonly List<int> _matches = [];
    private int _matchIndex = -1;
    private bool _scrollToLatest;
    /// <summary>视图可直接离屏渲染，窗口只负责承载与生命周期。</summary>
    public SshContainerLogView()
    {
        InitializeComponent();
        DataContextChanged += OnModelChanged;
        LogOutput.SelectionChanged += OnLogSelectionChanged; LogOutput.TextChanged += OnLogTextChanged;
        LogOutput.LayoutUpdated += OnLogLayoutUpdated;
    }
    private void OnModelChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_model is not null) _model.PropertyChanged -= OnDocumentChanged;
        _model = args.NewValue as SshContainerLogViewModel;
        if (_model is not null) _model.PropertyChanged += OnDocumentChanged;
        ApplyFont();
        Find(false);
    }
    /// <summary>供离屏验收与宿主使用的原生只读文本。</summary>
    public TextBox LogOutput => LogEditor.TextInputControl;
    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SshContainerLogViewModel.WrapLines)) LogOutput.TextWrapping = _model?.WrapLines == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        else if (args.PropertyName == nameof(SshContainerLogViewModel.LogFontSize)) ApplyFont();
    }
    private void OnMore(object sender, RoutedEventArgs args)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            ((MenuItem)menu.Items[0]).IsEnabled = LogOutput.SelectionLength > 0;
            menu.PlacementTarget = button; menu.IsOpen = true;
        }
    }
    private void OnCopySelection(object sender, RoutedEventArgs args)
    {
        if (LogOutput.SelectionLength == 0) return;
        try { Clipboard.SetText(LogOutput.SelectedText); } catch (ExternalException) { _model?.ReportLocalError(); }
    }
    private void OnFontIncrease(object sender, RoutedEventArgs args) => SetFont(Math.Min(24, LogOutput.FontSize + 1));
    private void OnFontDecrease(object sender, RoutedEventArgs args) => SetFont(Math.Max(10, LogOutput.FontSize - 1));
    private void SetFont(double size)
    {
        if (_model is not null) _model.LogFontSize = (int)size;
        else ApplyFont();
    }
    private void ApplyFont()
    {
        int size = _model?.LogFontSize ?? 13;
        LogOutput.FontSize = size; TextBlock.SetLineHeight(LogOutput, size + 10);
        var numbers = (TextBlock)LogEditor.FindName("LineNumbers"); numbers.FontSize = Math.Max(11, size - 2); numbers.LineHeight = size + 10;
    }
    private async void OnTailChanged(object sender, SelectionChangedEventArgs args)
    {
        if (IsLoaded && _model?.RefreshCommand.CanExecute(null) == true) await _model.RefreshCommand.ExecuteAsync();
    }
    private void OnSearchChanged(object sender, TextChangedEventArgs args) => Find(false);
    private void OnLogTextChanged(object sender, TextChangedEventArgs args) { Find(false); _scrollToLatest = SearchInput.Text.Length == 0; }
    private void OnLogLayoutUpdated(object? sender, EventArgs args)
    {
        if (!_scrollToLatest || !LogOutput.IsArrangeValid) return;
        _scrollToLatest = false; LogOutput.ScrollToEnd();
    }
    private void OnPrevious(object sender, RoutedEventArgs args) => Find(true, false);
    private void OnNext(object sender, RoutedEventArgs args) => Find(true);
    /// <summary>索引基于当前快照，搜索仅选择命中，不过滤或重写原文。</summary>
    private void Find(bool advance, bool forward = true)
    {
        if (LogOutput is null || SearchInput is null || MatchCount is null) return;
        string query = SearchInput.Text, text = LogOutput.Text;
        if (!advance)
        {
            _matches.Clear(); _matchIndex = -1;
            if (query.Length > 0)
                for (int start = 0; start <= text.Length - query.Length;)
                {
                    int index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) break;
                    _matches.Add(index); start = index + query.Length;
                }
        }
        if (_matches.Count > 0)
        {
            _matchIndex = advance ? (_matchIndex + (forward ? 1 : -1) + _matches.Count) % _matches.Count : 0;
            LogOutput.Select(_matches[_matchIndex], query.Length);
            LogEditor.RevealCharacter(_matches[_matchIndex]);
        }
        else LogOutput.Select(Math.Min(LogOutput.SelectionStart, text.Length), 0);
        MatchCount.Text = Text.Format("SshSearchMatches", _matchIndex + 1, _matches.Count);
    }
    private void OnLogSelectionChanged(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        _model.Assistant.Selection = LogOutput.SelectedText; _model.Assistant.SelectionStart = LogOutput.SelectionStart; _model.Assistant.DisplayedText = LogOutput.Text;
    }
    private void OnCopy(object sender, RoutedEventArgs args)
    {
        if (_model?.LogText.Length is not > 0) return;
        try { Clipboard.SetText(_model.LogText); } catch (ExternalException) { _model.ReportLocalError(); }
    }
    /// <summary>只有用户点击并确认目标文件才导出，不自动保存日志或分析结果。</summary>
    private void OnExport(object sender, RoutedEventArgs args)
    {
        if (_model?.LogText.Length is not > 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "container.log", DefaultExt = ".log", Filter = Text.Get("SshLogExportFilter"), OverwritePrompt = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, _model.LogText, new System.Text.UTF8Encoding(false)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _model.ReportLocalError(); }
    }
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchInput.Focus(); SearchInput.SelectAll(); args.Handled = true; }
        else if (args.Key == Key.F3) { Find(true, !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); args.Handled = true; }

    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_model is not null) _model.PropertyChanged -= OnDocumentChanged;
        _model = null; DataContextChanged -= OnModelChanged;
        LogOutput.LayoutUpdated -= OnLogLayoutUpdated; LogOutput.SelectionChanged -= OnLogSelectionChanged; LogOutput.TextChanged -= OnLogTextChanged;
    }
}
