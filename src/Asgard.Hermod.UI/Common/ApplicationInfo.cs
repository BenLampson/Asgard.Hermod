namespace Asgard.Hermod.UI.Common;

/// <summary>首页与关于页共享公开版本和固定项目地址，不从用户配置读取启动目标。</summary>
public static class ApplicationInfo
{
    /// <summary>当前 UI 程序集版本。</summary>
    public static string Version => "v" + (typeof(ApplicationInfo).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
    /// <summary>官方公开源码仓库地址。</summary>
    public static string RepositoryUrl => "https://github.com/BenLampson/Asgard.Hermod";
}
