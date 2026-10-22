namespace Asgard.Hermod.Abstractions.Models;

/// <summary>表单和保存服务共用字段边界，具体服务商自行实现网络认证。</summary>
public static class InstanceFieldValidation
{
    /// <summary>空文本代表校验通过；不会把填写值拼接到错误消息。</summary>
    public static string GetError(InstanceConfigurationField field, string? value)
    {
        ArgumentNullException.ThrowIfNull(field);
        string text = field.PreserveWhitespace ? value ?? "" : value?.Trim() ?? "";
        if (text.Length == 0) return field.IsRequired ? Text.Format("Enter", field.Label) : "";
        if (field.MaxLength > 0 && text.Length > field.MaxLength)
            return Text.Format("CannotExceedCharacters", field.Label, field.MaxLength);
        if (text.Any(char.IsControl)) return Text.Format("CannotContainControlCharacters", field.Label);
        if (field.Kind == InstanceFieldKind.Choice && !field.Choices.Any(choice => choice.Id == text))
            return Text.Format("Enter", field.Label);
        if (field.ValidationRule is InstanceFieldValidationRule.Port or InstanceFieldValidationRule.ConnectionTimeout)
        {
            int maximum = field.ValidationRule == InstanceFieldValidationRule.Port ? 65535 : 300;
            if (!int.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int number) || number < 1 || number > maximum)
                return Text.Get(field.ValidationRule == InstanceFieldValidationRule.Port ? "SshPortInvalid" : "SshTimeoutInvalid");
        }
        if (field.ValidationRule == InstanceFieldValidationRule.Host
            && !System.Net.IPAddress.TryParse(text, out _)
            && (Uri.CheckHostName(text) != UriHostNameType.Dns || text.Any(char.IsWhiteSpace)
                || text.IndexOfAny(['/', ':', '@', '?', '#']) >= 0))
            return Text.Get("SshHostInvalid");
        if (field.ValidationRule == InstanceFieldValidationRule.AsciiLettersAndDigits
            && !text.All(char.IsAsciiLetterOrDigit))
            return Text.Format("CanContainOnlyEnglishLettersAndDigits", field.Label);
        if (field.ValidationRule == InstanceFieldValidationRule.AsciiDigits && !text.All(char.IsAsciiDigit))
            return Text.Format("CanContainOnlyDigits", field.Label);
        return "";
    }

    /// <summary>统一计算条件字段适用性，UI 和持久化使用同一协议条件。</summary>
    public static bool IsApplicable(InstanceConfigurationField field, IReadOnlyDictionary<string, string> parameters) =>
        (field.VisibleWhenKey is null || parameters.GetValueOrDefault(field.VisibleWhenKey) == field.VisibleWhenValue)
        && field.Conditions.All(condition => parameters.GetValueOrDefault(condition.Key) == condition.Value);
}
