using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.Ssh.UI.Services;

/// <summary>SSH 模块引用公共连接信息插槽，公共窗口不引用 SSH 协议。</summary>
public sealed class SshConfigurationContentProvider : IInstanceConfigurationContentProvider
{
    /// <inheritdoc />
    public string TypeId => SshIds.Type;
    /// <inheritdoc />
    public Size PreferredWizardSize => new(912, 720);
    /// <inheritdoc />
    public FrameworkElement CreateContent(NewInstanceViewModel model) => new SshConnectionEditor(model);
}
