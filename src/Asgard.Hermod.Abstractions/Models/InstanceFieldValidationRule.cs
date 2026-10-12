namespace Asgard.Hermod.Abstractions.Models;

/// <summary>连接器声明的本地字符规则，网络权限验证仍由具体连接器负责。</summary>
public enum InstanceFieldValidationRule
{
    /// <summary>允许普通非控制字符。</summary>
    None,
    /// <summary>只允许 ASCII 字母和数字，例如 OSS AccessKey ID。</summary>
    AsciiLettersAndDigits,
}
