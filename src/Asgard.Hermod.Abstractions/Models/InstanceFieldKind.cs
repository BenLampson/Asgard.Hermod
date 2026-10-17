namespace Asgard.Hermod.Abstractions.Models;

/// <summary>共享配置表单支持的输入方式，密钥始终通过遮蔽控件编辑。</summary>
public enum InstanceFieldKind
{
    /// <summary>普通文字或地址。</summary>
    Text,
    /// <summary>密钥或令牌。</summary>
    Secret,
    /// <summary>稳定标识对应的选择选项。</summary>
    Choice,
    /// <summary>本机文件引用，文件内容不进入实例配置。</summary>
    File,
}
