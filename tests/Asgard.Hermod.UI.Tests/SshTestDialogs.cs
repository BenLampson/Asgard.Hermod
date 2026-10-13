using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.UI.Tests;

/// <summary>控制活动会话的关闭确认并记录是否真正请求过用户选择。</summary>
internal sealed class SshTestDialogs : ISshWorkspaceDialogs, IInstanceEditor
{
    internal bool Accept { get; set; }
    internal int Calls { get; private set; }
    internal InstanceSummary? Updated { get; set; }
    public Task<bool> ConfirmCloseAsync(string instanceName) { Calls++; return Task.FromResult(Accept); }
    public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    public Task<InstanceSummary?> EditAsync(string instanceId) => Task.FromResult(Updated);
}
