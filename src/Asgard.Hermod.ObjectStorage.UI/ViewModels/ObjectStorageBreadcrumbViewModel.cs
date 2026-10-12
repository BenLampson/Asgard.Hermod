namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>当前 Bucket 到目录前缀的可点击路径段。</summary>
public sealed class ObjectStorageBreadcrumbViewModel
{
    /// <summary>每个路径段复用页面导航，取消和陈旧结果由页面处理。</summary>
    public ObjectStorageBreadcrumbViewModel(string name, string prefix, Func<Task> navigate, bool isCurrent = false)
    {
        Name = name;
        Prefix = prefix;
        IsCurrent = isCurrent;
        NavigateCommand = new AsyncRelayCommand(_ => navigate());
    }
    /// <summary>显示 Bucket 或当前目录名称。</summary>
    public string Name { get; }
    /// <summary>当前路径段对应的完整目录前缀。</summary>
    public string Prefix { get; }
    /// <summary>当前路径段使用强调文字，父路径保持可点击。</summary>
    public bool IsCurrent { get; }
    /// <summary>进入路径段，清空旧的详情和批量勾选。</summary>
    public AsyncRelayCommand NavigateCommand { get; }
}
