using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.UI.Tests;

/// <summary>控制活动会话的关闭确认并记录是否真正请求过用户选择。</summary>
internal sealed class SshTestDialogs : ISshWorkspaceDialogs, IInstanceEditor
{
    internal bool Accept { get; set; }
    internal int Calls { get; private set; }
    internal InstanceSummary? Updated { get; set; }
    internal (int Consoles, int Forwards, int Transfers)? InstanceCloseImpact { get; private set; }
    public Task<bool> ConfirmCloseAsync(string instanceName) { Calls++; return Task.FromResult(Accept); }
    public Task<bool> ConfirmInstanceCloseAsync(string instanceName, int consoles, int forwards, int transfers)
    { InstanceCloseImpact = (consoles, forwards, transfers); return ConfirmCloseAsync(instanceName); }
    public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    public Task<InstanceSummary?> EditAsync(string instanceId) => Task.FromResult(Updated);
}
