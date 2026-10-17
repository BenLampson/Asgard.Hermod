namespace Asgard.Hermod.Abstractions.Models;

/// <summary>由具体连接器声明参数，通用向导不假设 Docker、存储等使用同一种凭据。</summary>
public sealed record InstanceConfigurationField(string Key, string Label, InstanceFieldKind Kind = InstanceFieldKind.Text,
    bool IsRequired = true, int MaxLength = 0, string Hint = "", bool IsSensitive = false,
    InstanceFieldValidationRule ValidationRule = InstanceFieldValidationRule.None)
{
    /// <summary>新建字段的协议默认值，不依赖界面语言。</summary>
    public string DefaultValue { get; init; } = "";
    /// <summary>认证密码可保留首尾空格；默认保持已有连接器的去空格行为。</summary>
    public bool PreserveWhitespace { get; init; }
    /// <summary>选项的标识参与协议，名称仅供展示。</summary>
    public IReadOnlyList<InstanceFieldChoice> Choices { get; init; } = [];
    /// <summary>依赖字段标识；为空时始终适用。</summary>
    public string? VisibleWhenKey { get; init; }
    /// <summary>依赖字段匹配这一稳定值时才参与显示、校验和保存。</summary>
    public string? VisibleWhenValue { get; init; }
    /// <summary>全部附加条件成立时才显示、校验及保存，例如连接路径与认证方式。</summary>
    public IReadOnlyList<InstanceFieldCondition> Conditions { get; init; } = [];
}
