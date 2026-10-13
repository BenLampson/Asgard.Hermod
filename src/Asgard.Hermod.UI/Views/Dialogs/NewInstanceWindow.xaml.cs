using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>添加实例五步向导，仅处理窗口、控件选择和焦点生命周期。</summary>
public partial class NewInstanceWindow : HermodDialog
{
    private readonly NewInstanceViewModel _model;

    /// <summary>绑定由宿主提供的真实连接器和持久化状态。</summary>
    public NewInstanceWindow(NewInstanceViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        model.PropertyChanged += OnModelPropertyChanged;
        Loaded += OnLoaded;
    }

    /// <summary>保存事务正在提交时暂缓关闭，避免保存成功却返回取消结果。</summary>
    protected override void OnClosing(CancelEventArgs args)
    {
        if (_model.IsSaving)
        {
            args.Cancel = true;
            return;
        }
        base.OnClosing(args);
        if (!args.Cancel)
            _model.Dispose();
    }

    /// <summary>窗口结束后取消连接测试并释放全部业务事件订阅。</summary>
    protected override void OnClosed(EventArgs args)
    {
        Loaded -= OnLoaded;
        _model.CloseRequested -= OnCloseRequested;
        _model.PropertyChanged -= OnModelPropertyChanged;
        _model.Dispose();
        base.OnClosed(args);
    }

    /// <summary>共享单选卡的鼠标与键盘选择同步到服务商列表。</summary>
    private void OnProviderChecked(object sender, RoutedEventArgs args)
    {
        if (sender is SelectionCard { DataContext: InstanceProviderDescriptor provider })
            ProviderList.SelectedItem = provider;
    }

    /// <summary>顶层类型选择使用独立卡片集合，不把 Docker 等类型放进存储服务商列表。</summary>
    private void OnTypeChecked(object sender, RoutedEventArgs args)
    {
        if (sender is SelectionCard { DataContext: InstanceTypeDescriptor type })
            TypeList.SelectedItem = type;
    }

    /// <summary>窗口加载后聚焦当前步骤的首个操作。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args) => FocusCurrentStep();

    /// <summary>先等待正文绑定切换可见性，再将键盘焦点交给新步骤。</summary>
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NewInstanceViewModel.CurrentStep))
            Dispatcher.InvokeAsync(FocusCurrentStep, DispatcherPriority.Loaded);
    }

    /// <summary>保持键盘导航与当前展示步骤一致。</summary>
    private void FocusCurrentStep()
    {
        if (!IsVisible)
            return;
        Control target = _model.CurrentStep switch
        {
            1 => _model.IsEditing ? ContinueButton : TypeList,
            2 => NameInput,
            3 => StartTestButton,
            4 => ContinueButton,
            _ => ContinueButton,
        };
        target.Focus();
    }

    /// <summary>持久化成功才返回确认结果，取消则返回 false。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;
}
