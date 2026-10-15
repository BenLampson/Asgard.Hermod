using System.Collections.Specialized;
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
    /// <summary>视图可直接离屏渲染，窗口只负责承载与生命周期。</summary>
    public SshContainerLogView()
    {
        InitializeComponent();
        DataContextChanged += OnModelChanged;
    }
    private void OnModelChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_model is not null) _model.Assistant.Messages.CollectionChanged -= OnMessageAdded;
        _model = args.NewValue as SshContainerLogViewModel;
        if (_model is not null) _model.Assistant.Messages.CollectionChanged += OnMessageAdded;
        Find(false);
    }
    private void OnMessageAdded(object? sender, NotifyCollectionChangedEventArgs args) => Dispatcher.BeginInvoke(() => AnalysisScroll.ScrollToEnd());
    private void OnSearchChanged(object sender, TextChangedEventArgs args) => Find(false);
    private void OnLogTextChanged(object sender, TextChangedEventArgs args) => Find(false);
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
            int line = LogOutput.GetLineIndexFromCharacterIndex(_matches[_matchIndex]); if (line >= 0) LogOutput.ScrollToLine(line);
        }
        else LogOutput.Select(Math.Min(LogOutput.SelectionStart, text.Length), 0);
        MatchCount.Text = Text.Format("SshSearchMatches", _matchIndex + 1, _matches.Count);
    }
    private void OnLogSelectionChanged(object sender, RoutedEventArgs args) { if (_model is not null) _model.Assistant.Selection = LogOutput.SelectedText; }
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
        else if (args.Key == Key.Enter && AnalysisPrompt.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.Control && _model?.Assistant.SendCommand.CanExecute(null) == true)
        { _model.Assistant.SendCommand.Execute(null); args.Handled = true; }
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_model is not null) _model.Assistant.Messages.CollectionChanged -= OnMessageAdded;
        _model = null; DataContextChanged -= OnModelChanged;
    }
}
