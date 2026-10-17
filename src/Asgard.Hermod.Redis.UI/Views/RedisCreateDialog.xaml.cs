using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Views;

/// <summary>公共窗口承载新增草稿；只在服务端确认成功后关闭，错误保留所有输入。</summary>
public partial class RedisCreateDialog : HermodDialog
{
    private readonly RedisCreateViewModel _model;
    /// <summary>协议提交与校验属于视图模型，视图负责焦点、列布局及关闭。</summary>
    public RedisCreateDialog(RedisCreateViewModel model)
    {
        _model = model; InitializeComponent(); DataContext = model;
        model.PropertyChanged += OnModelChanged; model.Created += OnCreated;
        Loaded += (_, _) => KeyInput.Focus();
        Closed += (_, _) => { model.PropertyChanged -= OnModelChanged; model.Created -= OnCreated; };
        UpdateColumns();
    }
    /// <summary>成功或主动核对冲突键时返回原始键身份。</summary>
    public RedisKeyId? ResultKey { get; private set; }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(RedisCreateViewModel.HasName) or nameof(RedisCreateViewModel.HasValue) or nameof(RedisCreateViewModel.HasScore)) UpdateColumns(); }
    private void UpdateColumns()
    {
        NameColumn.Visibility = _model.HasName ? Visibility.Visible : Visibility.Collapsed; NameColumn.Header = _model.NameLabel;
        ValueColumn.Visibility = _model.HasValue ? Visibility.Visible : Visibility.Collapsed;
        ScoreColumn.Visibility = _model.HasScore ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnCreated() { ResultKey = _model.CreatedKey; Accept(); }
    private void OnInspect(object sender, RoutedEventArgs args) { ResultKey = _model.SubmittedKey; Accept(); }
    private void Accept() { if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true; else Close(); }
    private void OnClosing(object? sender, CancelEventArgs args) { if (_model.IsBusy) args.Cancel = true; }
}
