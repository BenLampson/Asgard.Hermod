namespace Asgard.Hermod.ObjectStorage.UI.Views.Pages;

/// <summary>组合对象浏览器，页签首次激活时懒加载，关闭由工作区生命周期管理。</summary>
public partial class ObjectStorageView : UserControl
{
    private ObjectStorageWorkspaceViewModel? _observedModel;
    private double _detailsWidth = 278;
    private bool _detailsWereOpen = true;

    /// <summary>加载真实数据，重复切回页签复用已经初始化的状态。</summary>
    public ObjectStorageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => ObserveModel(null);
        DataContextChanged += (_, _) => { if (IsLoaded) ObserveModel(DataContext as ObjectStorageWorkspaceViewModel); };
        SizeChanged += (_, _) => ApplyDetailsLayout();
    }
    /// <summary>初始化失败留在页面中，并可通过重试恢复。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        ObserveModel(DataContext as ObjectStorageWorkspaceViewModel);
        if (DataContext is ObjectStorageWorkspaceViewModel model && !model.IsClosed)
            await model.InitializeAsync();
    }

    /// <summary>只订阅当前可见页面模型，缓存视图卸载后释放事件引用。</summary>
    private void ObserveModel(ObjectStorageWorkspaceViewModel? model)
    {
        if (_observedModel is not null) _observedModel.PropertyChanged -= OnModelPropertyChanged;
        _observedModel = model;
        if (model is not null) model.PropertyChanged += OnModelPropertyChanged;
        ApplyDetailsLayout();
    }

    /// <summary>模型控制收起，列宽及拖动属于 WPF 展示状态。</summary>
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ObjectStorageWorkspaceViewModel.IsDetailsOpen)) ApplyDetailsLayout();
    }

    /// <summary>收起释放所有详情宽度，重新展开恢复拖动后的宽度；窄窗口限制详情不挤出列表。</summary>
    private void ApplyDetailsLayout()
    {
        bool open = (DataContext as ObjectStorageWorkspaceViewModel)?.IsDetailsOpen ?? true;
        if (!open)
        {
            if (_detailsWereOpen && DetailsColumn.ActualWidth > 0) _detailsWidth = DetailsColumn.ActualWidth;
            DetailsColumn.MinWidth = 0; DetailsColumn.MaxWidth = double.PositiveInfinity;
            DetailsColumn.Width = new GridLength(0); SplitterColumn.Width = new GridLength(0);
            ListColumn.MinWidth = 0; _detailsWereOpen = false;
            return;
        }
        // 内部 Grid 在列宽下限冲突时可能超出父视口，必须按视图实际宽度收敛。
        double width = ActualWidth;
        if (width <= 0) return;
        ListColumn.MinWidth = Math.Min(260, Math.Max(0, width - 226));
        double maximum = Math.Min(560, Math.Max(220, width - ListColumn.MinWidth - 6));
        DetailsColumn.MinWidth = 220; DetailsColumn.MaxWidth = maximum;
        double preferred = _detailsWereOpen ? DetailsColumn.Width.Value : _detailsWidth;
        DetailsColumn.Width = new GridLength(Math.Clamp(preferred, 220, maximum));
        SplitterColumn.Width = new GridLength(6); _detailsWereOpen = true;
    }
}
