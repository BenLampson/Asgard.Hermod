namespace Asgard.Hermod.Abstractions.Models;

/// <summary>表单和保存服务共用字段边界，具体服务商自行实现网络认证。</summary>
public static class InstanceFieldValidation
{
    /// <summary>空文本代表校验通过；不会把填写值拼接到错误消息。</summary>
    public static string GetError(InstanceConfigurationField field, string? value)
    {
        ArgumentNullException.ThrowIfNull(field);
        string text = value?.Trim() ?? "";
        if (text.Length == 0) return field.IsRequired ? $"请输入 {field.Label}。" : "";
        if (field.MaxLength > 0 && text.Length > field.MaxLength)
            return $"{field.Label} 不能超过 {field.MaxLength} 个字符。";
        if (text.Any(char.IsControl)) return $"{field.Label} 不能包含控制字符。";
        if (field.ValidationRule == InstanceFieldValidationRule.AsciiLettersAndDigits
            && !text.All(char.IsAsciiLetterOrDigit))
            return $"{field.Label} 只能包含英文字母和数字。";
        return "";
    }
}
