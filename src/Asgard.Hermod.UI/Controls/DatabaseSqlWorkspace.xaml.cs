namespace Asgard.Hermod.UI.Controls;

/// <summary>DBWindow 的 SQL 母组件：固定工具栏、可调编辑器与唯一输出槽。</summary>
public partial class DatabaseSqlWorkspace : UserControl
{
    /// <summary>模块拥有的真实结果视图。</summary>
    public static readonly DependencyProperty ResultContentProperty = DependencyProperty.Register(nameof(ResultContent), typeof(object), typeof(DatabaseSqlWorkspace));
    /// <summary>PostgreSQL 当前仅提供估算，不能显示实际执行入口。</summary>
    public static readonly DependencyProperty SupportsActualAnalysisProperty = DependencyProperty.Register(nameof(SupportsActualAnalysis), typeof(bool), typeof(DatabaseSqlWorkspace), new PropertyMetadata(true));
    /// <summary>当前结果。</summary>
    public object? ResultContent { get => GetValue(ResultContentProperty); set => SetValue(ResultContentProperty, value); }
    /// <summary>是否支持实际分析。</summary>
    public bool SupportsActualAnalysis { get => (bool)GetValue(SupportsActualAnalysisProperty); set => SetValue(SupportsActualAnalysisProperty, value); }
    /// <summary>真实 SQL 编辑器，提供选区与光标给协议模块。</summary>
    public CodeEditor Editor => SqlEditor;
    /// <summary>用户选择的执行数据库。</summary>
    public string? SelectedDatabase => TargetSelector.SelectedItem as string;
    /// <summary>运行当前或选中语句。</summary>
    public event RoutedEventHandler? RunRequested;
    /// <summary>运行完整文稿。</summary>
    public event RoutedEventHandler? RunAllRequested;
    /// <summary>读取估算计划。</summary>
    public event RoutedEventHandler? ExplainRequested;
    /// <summary>确认后实际分析。</summary>
    public event RoutedEventHandler? AnalyzeRequested;
    /// <summary>目标变更由协议模块处理草稿和连接。</summary>
    public event SelectionChangedEventHandler? TargetChanged;
    /// <summary>复制当前输出。</summary>
    public event RoutedEventHandler? CopyRequested;
    /// <summary>导出当前输出。</summary>
    public event RoutedEventHandler? ExportRequested;
    /// <summary>构造纯布局，不自动执行、连接或读取数据库。</summary>
    public DatabaseSqlWorkspace()
    {
        InitializeComponent();
        SizeChanged += (_, _) => SqlRow.MaxHeight = Math.Max(120, ActualHeight - 246);
    }
    private void OnRun(object sender, RoutedEventArgs args) => RunRequested?.Invoke(this, args);
    private void OnRunAll(object sender, RoutedEventArgs args) => RunAllRequested?.Invoke(this, args);
    private void OnExplain(object sender, RoutedEventArgs args) => ExplainRequested?.Invoke(this, args);
    private void OnAnalyze(object sender, RoutedEventArgs args) => AnalyzeRequested?.Invoke(this, args);
    private void OnTargetChanged(object sender, SelectionChangedEventArgs args) { if (IsLoaded) TargetChanged?.Invoke(this, args); }
    private void OnCopy(object sender, RoutedEventArgs args) => CopyRequested?.Invoke(this, args);
    private void OnExport(object sender, RoutedEventArgs args) => ExportRequested?.Invoke(this, args);
    private void OnAnalysisMenu(object sender, RoutedEventArgs args) => OpenMenu(AnalyzeButton);
    private void OnAnalysisOpened(object sender, RoutedEventArgs args)
    { if (sender is ContextMenu menu && menu.Items.Count > 1 && menu.Items[1] is MenuItem actual) actual.Visibility = SupportsActualAnalysis ? Visibility.Visible : Visibility.Collapsed; }
    private void OnRunMenu(object sender, MouseButtonEventArgs args) { args.Handled = true; OpenMenu(RunButton); }
    private static void OpenMenu(Button button)
    {
        if (button.ContextMenu is not { } menu) return;
        menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.VerticalOffset = 4; menu.IsOpen = true;
    }
}
