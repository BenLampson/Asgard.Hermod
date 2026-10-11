namespace Asgard.Hermod.UI.Tests;

/// <summary>每个用例使用独立界面状态和连接替身，仅复用真实 WPF 应用资源。</summary>
internal sealed class ShellTestEnvironment : IAsyncDisposable
{
    /// <summary>构造外壳时不访问网络、不创建实际 Shell。</summary>
    internal ShellTestEnvironment()
    {
        Source = new TestWorkspaceDataSource();
        Editor = new FakeInstanceEditor();
        Factory = new FakeTerminalSessionFactory();
        Themes = new ThemeService(Application.Current.Resources);
        Themes.SetTheme(UiTheme.Light);
        Shell = new ShellViewModel(Source, Editor, Factory, Themes);
    }

    /// <summary>当前用例的导航数据。</summary>
    internal TestWorkspaceDataSource Source { get; }

    /// <summary>当前用例的实例编辑边界。</summary>
    internal FakeInstanceEditor Editor { get; }

    /// <summary>当前用例的终端创建与清理边界。</summary>
    internal FakeTerminalSessionFactory Factory { get; }

    /// <summary>使用已打包的主题资源服务。</summary>
    internal ThemeService Themes { get; }

    /// <summary>待验证的真实外壳状态。</summary>
    internal ShellViewModel Shell { get; }

    /// <summary>从导航状态中取得本地实例，不依赖固定的排列位置。</summary>
    internal InstanceItemViewModel Local => Shell.Sidebar.AllInstances.Single(item => item.IsLocal);

    /// <summary>绑定内容宿主，使用实际的页面缓存与终端控件。</summary>
    internal WorkspaceContentHost CreateContentHost()
    {
        var host = new WorkspaceContentHost { DataContext = Shell.Workspace };
        host.SetBinding(WorkspaceContentHost.PageProperty, new Binding(nameof(WorkspaceViewModel.SelectedTab)));
        host.Measure(new Size(1000, 700));
        host.Arrange(new Rect(0, 0, 1000, 700));
        return host;
    }

    /// <summary>无论断言成功或失败都放行会话清理，并解除服务订阅。</summary>
    public async ValueTask DisposeAsync()
    {
        Factory.ReleaseAllDisposals();
        await Shell.DisposeAsync();
        await Shell.Workspace.DisposeAsync();
        await WpfTestFixture.DrainDispatcherAsync();
    }
}
