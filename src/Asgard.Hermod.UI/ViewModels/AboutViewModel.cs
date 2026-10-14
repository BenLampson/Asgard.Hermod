using System.Diagnostics;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>关于页展示公开软件信息；仅在用户点击时打开固定项目地址。</summary>
public sealed class AboutViewModel : ObservableObject
{
    /// <summary>创建显式浏览器入口，失败保留可手动复制的地址。</summary>
    public AboutViewModel() => OpenGitHubCommand = new RelayCommand(_ =>
    {
        try { Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true }); }
        catch (Exception) { Message = Text.Get("HomeOpenRepositoryFailed"); OnPropertyChanged(nameof(Message)); }
    });
    /// <summary>与首页一致的当前版本。</summary>
    public string Version => ApplicationInfo.Version;
    /// <summary>固定的公开源码地址。</summary>
    public string RepositoryUrl => ApplicationInfo.RepositoryUrl;
    /// <summary>安全本地化反馈。</summary>
    public string Message { get; private set; } = "";
    /// <summary>打开默认浏览器的用户动作。</summary>
    public RelayCommand OpenGitHubCommand { get; }
}
