using System.IO;
using System.Text;
using Asgard.Hermod.UI.Views.Dialogs;
using Microsoft.Win32;

namespace Asgard.Hermod.UI.Controls;

/// <summary>执行计划的单一阅读器，所有交互只针对已取得的快照。</summary>
public partial class DatabasePlanReader : UserControl
{
    private DatabaseExecutionPlanViewModel? _model;
    private DatabaseExecutionPlan? _preparedFields;
    private bool? _compact;

    /// <summary>创建离线阅读视图；不自动打开窗口、连接或查询。</summary>
    public DatabasePlanReader()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { _compact = null; Observe(DataContext as DatabaseExecutionPlanViewModel); };
        Loaded += (_, _) => Observe(DataContext as DatabaseExecutionPlanViewModel);
        Unloaded += (_, _) => Observe(null);
    }

    private void Observe(DatabaseExecutionPlanViewModel? model)
    {
        if (_model is not null) { _model.PropertyChanged -= OnModelChanged; _model.LocateRequested -= OnLocate; }
        _model = model;
        if (model is not null) { model.PropertyChanged += OnModelChanged; model.LocateRequested += OnLocate; }
        if (model is not null && !ReferenceEquals(_preparedFields, model.Snapshot))
        {
            GenericFields.Columns.Clear();
            for (int index = 0; index < model.Snapshot.Columns.Count; index++)
                GenericFields.Columns.Add(new DataGridTextColumn
                {
                    Header = model.Snapshot.Columns[index],
                    Width = new(180),
                    MinWidth = 80,
                    Binding = new Binding($"[{index}]") { TargetNullValue = Text.Get("PlanSqlNull") }
                });
            _preparedFields = model.Snapshot;
        }
        UpdateReaderLayout();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DatabaseExecutionPlanViewModel.IsDetailsVisible) or nameof(DatabaseExecutionPlanViewModel.ViewMode)) UpdateReaderLayout();
    }
    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args) => UpdateReaderLayout();
    private void UpdateReaderLayout()
    {
        if (ReaderBody is null) return;
        if (_model is not null && ReaderBody.ActualWidth > 0)
        {
            bool compact = ReaderBody.ActualWidth < 1000;
            if (_compact != compact) { _compact = compact; if (compact) _model.IsDetailsOpen = false; }
        }
        DetailsColumn.Width = new(_model?.IsDetailsVisible == true ? 296 : 0);
        double available = Math.Max(68, ReaderBody.ActualHeight - 32 - (_model?.HasFlowSummary == true ? 42 : 0));
        NodeRows.MaxHeight = FieldRows.MaxHeight = available;
        SearchPanel.Width = Math.Max(120, Math.Min(400, ReaderBody.ActualWidth - 36));
        var list = _model?.IsTree == true ? NodeRows : FieldRows;
        AlignMetricHeader(Descendant<ScrollViewer>(list));
    }
    private void OnGraphScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        if (args.OriginalSource is ScrollViewer viewer) AlignMetricHeader(viewer);
    }
    private void AlignMetricHeader(ScrollViewer? viewer) => MetricHeader.Margin = new(18, 0,
        18 + (viewer?.ComputedVerticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0), 0);
    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (Descendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private void OnNodeKeyDown(object sender, KeyEventArgs args)
    {
        if (_model?.SelectedNode is not { } selected) return;
        switch (args.Key)
        {
            case Key.Left:
                if (selected.HasChildren && selected.IsExpanded) selected.IsExpanded = false;
                else if (selected.Parent is not null) _model.SelectedNode = selected.Parent;
                args.Handled = true; break;
            case Key.Right:
                if (selected.HasChildren && !selected.IsExpanded) selected.IsExpanded = true;
                else if (selected.Children.Count > 0) _model.SelectedNode = selected.Children[0];
                args.Handled = true; break;
            case Key.Enter: _model.OpenDetailsCommand.Execute(null); args.Handled = true; break;
        }
    }
    private void OnReaderKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { OpenSearch(); args.Handled = true; }
        else if (args.Key == Key.Escape && SearchPanel.Visibility == Visibility.Visible) { SearchPanel.Visibility = Visibility.Collapsed; args.Handled = true; }
    }
    private void OnOpenSearch(object sender, RoutedEventArgs args) => OpenSearch();
    private void OpenSearch()
    {
        if (_model is null) return;
        _model.ViewMode = "raw"; SearchPanel.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { SearchInput.Focus(); SearchInput.SelectAll(); }));
    }
    private void OnCloseSearch(object sender, RoutedEventArgs args) => SearchPanel.Visibility = Visibility.Collapsed;
    private void OnSearchKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { FindMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); args.Handled = true; }
    }
    private void OnNextMatch(object sender, RoutedEventArgs args) => FindMatch(false);
    private void OnPreviousMatch(object sender, RoutedEventArgs args) => FindMatch(true);
    private void FindMatch(bool previous)
    {
        if (_model?.SearchText is not { Length: > 0 } term || _model.RawText.Length == 0) return;
        string text = _model.RawText; int found;
        if (previous)
        {
            int start = Math.Min(text.Length - 1, RawEditor.SelectionStart - 1);
            found = start < 0 ? -1 : text.LastIndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0) found = text.LastIndexOf(term, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            int start = Math.Min(text.Length, RawEditor.SelectionStart + RawEditor.SelectionLength);
            found = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (found < 0) found = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        }
        if (found >= 0) SelectRaw(found, term.Length);
    }
    private void OnLocate(int start, int length) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SelectRaw(start, length)));
    private void SelectRaw(int start, int length)
    {
        if (_model is null || start < 0 || start > _model.RawText.Length) return;
        RawEditor.Select(start, Math.Clamp(length, 0, _model.RawText.Length - start));
        int line = RawEditor.GetLineIndexFromCharacterIndex(start); if (line >= 0) RawEditor.ScrollToLine(line);
        RawEditor.Focus();
    }
    private void OnMore(object sender, RoutedEventArgs args)
    {
        if (MoreButton.ContextMenu is not { } menu) return;
        menu.DataContext = _model; menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.VerticalOffset = 4; menu.IsOpen = true;
    }
    private void OnCopyRaw(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        try { Clipboard.SetText(_model.RawText); }
        catch (System.Runtime.InteropServices.ExternalException) { ShowError(Text.Get("DatabaseClipboardFailed")); }
    }
    private async void OnExportRaw(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        string snapshot = _model.RawText;
        var dialog = new SaveFileDialog { FileName = Text.Get("PlanDefaultExportName"), DefaultExt = ".txt", Filter = Text.Get("PlanRawFileFilter"), OverwritePrompt = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await File.WriteAllTextAsync(dialog.FileName, snapshot, new UTF8Encoding(false)); }
        catch (Exception error) { ShowError(Text.Error(error, "PlanExportFailed")); }
    }
    private void OnShowSource(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        new MessageDialog(Text.Get("PlanShowSource"), Text.Format("PlanSourceContext", _model.Snapshot.Database, _model.Snapshot.Sql)) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private void ShowError(string message) => new MessageDialog(Text.Get("Hermod"), message) { Owner = Window.GetWindow(this) }.ShowDialog();
}
