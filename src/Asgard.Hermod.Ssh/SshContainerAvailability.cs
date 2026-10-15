namespace Asgard.Hermod.Ssh;

/// <summary>区分容器服务的无安装、权限及一般读取失败，不依赖错误文本判断。</summary>
public enum SshContainerAvailability
{
    /// <summary>命令成功，空列表也属于可用。</summary>
    Available,
    /// <summary>远端没有 docker 命令。</summary>
    NotInstalled,
    /// <summary>当前用户不能访问容器服务。</summary>
    AccessDenied,
    /// <summary>服务不可用或返回不完整输出。</summary>
    Unavailable,
}
