namespace Asgard.Hermod.Abstractions.Models;

/// <summary>连接器声明的本地字符规则，网络权限验证仍由具体连接器负责。</summary>
public enum InstanceFieldValidationRule
{
    /// <summary>允许普通非控制字符。</summary>
    None,
    /// <summary>只允许 ASCII 字母和数字，例如 OSS AccessKey ID。</summary>
    AsciiLettersAndDigits,
    /// <summary>TCP 端口，范围为 1 到 65535。</summary>
    Port,
    /// <summary>连接超时秒数，范围为 1 到 300。</summary>
    ConnectionTimeout,
    /// <summary>主机名或 IP 地址，不接收 URL 或端口。</summary>
    Host,
    /// <summary>只允许 ASCII 数字，保留输入中的前导零。</summary>
    AsciiDigits,
}
