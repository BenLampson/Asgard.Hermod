namespace Asgard.Hermod.Abstractions.Models;

/// <summary>由具体连接器声明参数，通用向导不假设 Docker、存储等使用同一种凭据。</summary>
public sealed record InstanceConfigurationField(string Key, string Label, InstanceFieldKind Kind = InstanceFieldKind.Text,
    bool IsRequired = true, int MaxLength = 0, string Hint = "", bool IsSensitive = false,
    InstanceFieldValidationRule ValidationRule = InstanceFieldValidationRule.None);
