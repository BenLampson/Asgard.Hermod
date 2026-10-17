using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Views;

/// <summary>工作页缓存此视图，切换键页签时保留原生编辑器滚动、选择和表格位置。</summary>
public partial class RedisKeyContentView : UserControl
{
    /// <summary>只有编辑弹窗显式启用可写控件，工作区实例始终只读。</summary>
    public static readonly DependencyProperty IsEditorHostProperty = DependencyProperty.Register(nameof(IsEditorHost), typeof(bool), typeof(RedisKeyContentView),
        new PropertyMetadata(false, (owner, _) => ((RedisKeyContentView)owner).UpdateColumns()));
    public bool IsEditorHost { get => (bool)GetValue(IsEditorHostProperty); set => SetValue(IsEditorHostProperty, value); }
    private RedisKeyTabViewModel? _model;
    /// <summary>列可见性跟随协议类型，而不是翻译后的标签。</summary>
    public RedisKeyContentView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnChanged; _model = null; };
    }
    private void Attach()
    {
        if (_model is not null) _model.PropertyChanged -= OnChanged;
        _model = DataContext as RedisKeyTabViewModel; if (_model is not null) _model.PropertyChanged += OnChanged; UpdateColumns();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(RedisKeyTabViewModel.Type) or nameof(RedisKeyTabViewModel.IsEditing) or nameof(RedisKeyTabViewModel.IsBusy)) UpdateColumns(); }
    private void UpdateColumns()
    {
        if (_model is null || StringEditor is null) return;
        StringEditor.IsReadOnly = !IsEditorHost || _model.IsReadOnly; CollectionGrid.IsReadOnly = StringEditor.IsReadOnly;
        KeyActions.Visibility = IsEditorHost ? Visibility.Collapsed : Visibility.Visible;
        RowActions.Visibility = IsEditorHost || _model.HasMore ? Visibility.Visible : Visibility.Collapsed;
        EditingHint.Visibility = IsEditorHost ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in RowActions.Children.OfType<Button>().Skip(1)) button.IsEnabled = IsEditorHost && !_model.IsBusy;
        RecordColumn.Visibility = _model.IsStream && !_model.IsEditing ? Visibility.Visible : Visibility.Collapsed;
        ValueColumn.Visibility = _model.HasValue ? Visibility.Visible : Visibility.Collapsed;
        ScoreColumn.Visibility = _model.HasScore ? Visibility.Visible : Visibility.Collapsed;
        NameColumn.IsReadOnly = _model.IsList;
    }
    private void OnCopyKey(object sender, RoutedEventArgs args) => Copy(_model?.FullName);
    private void OnCopyValue(object sender, RoutedEventArgs args) => Copy(_model?.GetDisplayedText());
    private void Copy(string? value)
    {
        if (value is null) return;
        try { Clipboard.SetText(value); }
        catch { _model?.ReportUiError(Text.Exception("CannotWriteToTheClipboardTryAgainLater")); }
    }
    private void OnKeyMenu(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = Text.Get("RedisRefreshValue"), Command = _model.RefreshCommand });
        menu.Items.Add(new MenuItem { Header = Text.Get("RedisChangeExpiry"), Command = _model.ExpiryCommand });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = Text.Get("RedisDeleteKey"), Command = _model.DeleteCommand });
        menu.PlacementTarget = (Button)sender; menu.IsOpen = true;
    }
}
