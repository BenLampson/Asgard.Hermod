namespace Asgard.Hermod.Abstractions.Services;

/// <summary>自动同步、账户隔离与三方合并的公共边界。</summary>
public interface IConfigurationSyncService
{
    /// <summary>稳定同步状态。</summary>
    ConfigurationSyncStatus Status { get; }
    /// <summary>可直接显示的本地化安全提示。</summary>
    string Message { get; }
    /// <summary>最后完整成功同步的 UTC 时间。</summary>
    DateTime? LastSyncedAt { get; }
    /// <summary>当前账户是否已经在本机保存同步密码。</summary>
    bool HasPassword { get; }
    /// <summary>当前版本中等待用户处理的冲突。</summary>
    IReadOnlyList<ConfigurationConflict> Conflicts { get; }
    /// <summary>后台状态与云端应用后的更新通知。</summary>
    event EventHandler? Changed;
    /// <summary>开始后台轮询，并恢复当前账户的工作区。</summary>
    Task StartAsync(CancellationToken cancellationToken = default);
    /// <summary>关闭应用时取消后台网络并等待安全结束。</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
    /// <summary>校验云端密码后在本机保护保存，随后自动合并同步。</summary>
    Task EnableAsync(string password, CancellationToken cancellationToken = default);
    /// <summary>忘记本机保存的密码，保留配置与同步基准。</summary>
    Task ForgetPasswordAsync(CancellationToken cancellationToken = default);
    /// <summary>立即检查云端；失败保留本机更改。</summary>
    Task SynchronizeAsync(CancellationToken cancellationToken = default);
    /// <summary>安全选择当前账户工作区，在本地编辑前调用。</summary>
    Task PrepareWorkspaceAsync(CancellationToken cancellationToken = default);
    /// <summary>本机修改与账户切换、同步应用串行执行。</summary>
    Task RunLocalChangeAsync(Func<Task> action, CancellationToken cancellationToken = default);
    /// <summary>提交成功的本机修改触发延迟同步。</summary>
    void RequestSync();
    /// <summary>按冲突键选择云端或本地；版本变化后重新计算而不沿用旧选择。</summary>
    Task ResolveAsync(IReadOnlyDictionary<string, bool> useCloud, CancellationToken cancellationToken = default);
}
