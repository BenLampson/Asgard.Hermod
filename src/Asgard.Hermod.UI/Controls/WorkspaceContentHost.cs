using Asgard.Hermod.UI.ViewModels;
using Asgard.Hermod.UI.Views.Pages;

namespace Asgard.Hermod.UI.Controls;

/// <summary>按工作区页面缓存视图，切换页签保留会话，关闭页签释放原生控件。</summary>
public sealed class WorkspaceContentHost : ContentControl, IDisposable
{
    /// <summary>声明当前显示页签的依赖属性。</summary>
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(WorkspacePageViewModel), typeof(WorkspaceContentHost),
        new PropertyMetadata(null, OnPageChanged));

    private readonly Dictionary<WorkspacePageViewModel, FrameworkElement> _views = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    /// <summary>获取或设置当前页面；同一页面再次激活时复用已有视图。</summary>
    public WorkspacePageViewModel? Page
    {
        get => (WorkspacePageViewModel?)GetValue(PageProperty);
        set
        {
            ObjectDisposedException.ThrowIf(_disposed && value is not null, this);
            SetValue(PageProperty, value);
        }
    }

    /// <summary>使缓存的页面填满工作区内容槽。</summary>
    public WorkspaceContentHost()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Focusable = false;
    }

    /// <summary>独立窗口接管同一标题元素，正文不保留重复工具栏，也不重建页面或控件。</summary>
    public WorkspaceHeader? TakeHeader()
    {
        if (Content is not FrameworkElement root) return null;
        var header = Detach(root);
        if (header is not null) header.DataContext = Page;
        return header;

        WorkspaceHeader? Detach(DependencyObject parent)
        {
            if (parent is WorkspacePageLayout { Toolbar: WorkspaceHeader toolbar } layout)
            { toolbar.DataContext = Page; layout.Toolbar = null; return toolbar; }
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>().ToArray())
            {
                if (child is WorkspaceHeader header && parent is Panel panel)
                { header.DataContext = Page; panel.Children.Remove(header); return header; }
                if (Detach(child) is { } found) return found;
            }
            return null;
        }
    }

    /// <summary>响应绑定的页面切换，仅为首次打开的页签创建视图。</summary>
    private static void OnPageChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var host = (WorkspaceContentHost)sender;
        if (host._disposed)
            return;
        if (args.NewValue is not WorkspacePageViewModel page || page.IsClosed)
        {
            host.Content = null;
            return;
        }
        if (!host._views.TryGetValue(page, out var view))
        {
            view = host.CreateView(page);
            view.DataContext = page;
            host._views.Add(page, view);
            page.PropertyChanged += host.OnPagePropertyChanged;
        }
        host.Content = view;
    }

    /// <summary>根据页面类型选择对应的原生 WPF 内容组件。</summary>
    private FrameworkElement CreateView(WorkspacePageViewModel page) => page switch
    {
        HomeWorkspaceViewModel => new HomeWorkspaceView(),
        EmptyWorkspaceViewModel => new EmptyWorkspaceView(),
        ServiceDetailViewModel => new ServiceDetailView(),
        TerminalWorkspaceViewModel => new TerminalWorkspaceView(),
        _ => CreateModuleView(page),
    };

    /// <summary>独立功能模块通过资源字典提供页面模板，外壳不引用模块实现。</summary>
    private FrameworkElement CreateModuleView(WorkspacePageViewModel page)
    {
        if (TryFindResource(new DataTemplateKey(page.GetType())) is DataTemplate template &&
            template.LoadContent() is FrameworkElement view)
            return view;
        throw new NotSupportedException(Text.Format("NoContentViewIsRegisteredForPageType", page.GetType().Name));
    }

    /// <summary>接收关闭通知；必要时切回 UI 线程移除原生控件。</summary>
    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not WorkspacePageViewModel page
            || (!string.IsNullOrEmpty(args.PropertyName) && args.PropertyName != nameof(WorkspacePageViewModel.IsClosed)))
            return;
        if (Dispatcher.CheckAccess())
            RemoveClosedPage(page);
        else
            Dispatcher.InvokeAsync(() => RemoveClosedPage(page));
    }

    /// <summary>关闭已缓存页面并解除订阅，视图模型由应用层继续负责释放。</summary>
    private void RemoveClosedPage(WorkspacePageViewModel page)
    {
        if (_disposed || !page.IsClosed || !_views.Remove(page, out var view))
            return;
        page.PropertyChanged -= OnPagePropertyChanged;
        if (ReferenceEquals(Content, view))
            Content = null;
        ReleaseView(view);
    }

    /// <summary>释放带有原生资源的页面并解除数据上下文引用。</summary>
    private static void ReleaseView(FrameworkElement view)
    {
        if (view is IDisposable disposable)
            disposable.Dispose();
        view.DataContext = null;
    }

    /// <summary>退出窗口时清理所有缓存与订阅，可重复调用。</summary>
    public void Dispose()
    {
        VerifyAccess();
        if (_disposed)
            return;
        _disposed = true;
        Content = null;
        BindingOperations.ClearBinding(this, PageProperty);
        SetCurrentValue(PageProperty, null);
        foreach (var (page, view) in _views)
        {
            page.PropertyChanged -= OnPagePropertyChanged;
            ReleaseView(view);
        }
        _views.Clear();
        GC.SuppressFinalize(this);
    }
}
