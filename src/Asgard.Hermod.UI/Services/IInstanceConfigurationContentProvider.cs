namespace Asgard.Hermod.UI.Services;

/// <summary>类型模块提供信息填写正文，仍复用公共入口、向导、校验和保存。</summary>
public interface IInstanceConfigurationContentProvider
{
    /// <summary>该正文服务的稳定实例类型。</summary>
    string TypeId { get; }
    /// <summary>类型表单的推荐窗口空间；小屏幕由公共窗口收敛到工作区。</summary>
    Size PreferredWizardSize => new(820, 600);
    /// <summary>创建绑定到公共向导状态的信息控件。</summary>
    FrameworkElement CreateContent(NewInstanceViewModel model);
}
