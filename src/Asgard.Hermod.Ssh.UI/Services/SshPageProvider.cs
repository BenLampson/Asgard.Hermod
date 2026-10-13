using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Services;

/// <summary>SSH 页面由模块提供，主框架不识别协议或凭据。</summary>
public sealed class SshPageProvider(IInstanceConfigurationService configurations, ISshConnectionFactory connections,
    IInstanceEditor editor, ISshWorkspaceDialogs dialogs) : IInstancePageProvider
{
    /// <inheritdoc />
    public string TypeId => SshIds.Type;
    /// <inheritdoc />
    public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) => new SshWorkspaceViewModel(instance, configurations, connections, editor, dialogs);
}
