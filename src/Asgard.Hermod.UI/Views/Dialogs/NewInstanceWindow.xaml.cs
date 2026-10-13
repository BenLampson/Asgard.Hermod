using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>独立类型入口与可复用连接向导，仅处理窗口、控件选择和焦点生命周期。</summary>
public partial class NewInstanceWindow : HermodDialog
{
    private readonly NewInstanceViewModel _model;
    private readonly IReadOnlyDictionary<string, IInstanceConfigurationContentProvider> _contents;

    /// <summary>绑定由宿主提供的真实连接器和持久化状态。</summary>
    public NewInstanceWindow(NewInstanceViewModel model, IEnumerable<IInstanceConfigurationContentProvider>? contents = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        _contents = (contents ?? []).ToDictionary(item => item.TypeId, StringComparer.Ordinal);
        InitializeComponent();
        DataContext = model;
        RefreshInformationContent();
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

    /// <summary>在列表处理 Escape 之前取消向导，保证类型卡片获得焦点时仍可关闭模态窗口。</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        if (args.Key == Key.Escape && _model.CancelCommand.CanExecute(null))
        {
            args.Handled = true;
            _model.CancelCommand.Execute(null);
            return;
        }
        base.OnPreviewKeyDown(args);
    }

    /// <summary>窗口加载后聚焦当前步骤的首个操作。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args) => FocusCurrentStep();

    /// <summary>先等待正文绑定切换可见性，再将键盘焦点交给新步骤。</summary>
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NewInstanceViewModel.SelectedType)) RefreshInformationContent();
        if (args.PropertyName == nameof(NewInstanceViewModel.CurrentStep))
            Dispatcher.InvokeAsync(() =>
            {
                (WizardLayout.Template.FindName("PART_BodyScroll", WizardLayout) as ScrollViewer)?.ScrollToTop();
                FocusCurrentStep();
            }, DispatcherPriority.Loaded);
    }

    /// <summary>在类型入口之后装配对应模块的正文，公共窗口不引用协议程序集。</summary>
    private void RefreshInformationContent()
    {
        var provider = _model.SelectedType is { } type ? _contents.GetValueOrDefault(type.Id) : null;
        ConnectionContent.InformationContent = provider?.CreateContent(_model);
        Size desired = provider?.PreferredWizardSize ?? new Size(820, 600);
        Width = Math.Min(desired.Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(desired.Height, SystemParameters.WorkArea.Height - 40);
    }

    /// <summary>保持键盘导航与当前展示步骤一致。</summary>
    private void FocusCurrentStep()
    {
        if (!IsVisible)
            return;
        if (!_model.HasTypeSelection)
            TypeList.Focus();
        else if (!ConnectionContent.FocusCurrentStep(_model.CurrentStep, _model.IsEditing))
            ContinueButton.Focus();
    }

    /// <summary>持久化成功才返回确认结果，取消则返回 false。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => DialogResult = accepted;
}
