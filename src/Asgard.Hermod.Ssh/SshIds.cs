namespace Asgard.Hermod.Ssh;

/// <summary>SSH 配置的稳定协议标识，不使用翻译后的文字做判断。</summary>
public static class SshIds
{
    /// <summary>SSH 顶层实例类型。</summary>
    public const string Type = "ssh";
    /// <summary>直连配置。</summary>
    public const string Direct = "ssh-direct";
    /// <summary>单跳 SSH 路径。</summary>
    public const string Jump = "ssh-jump";
    /// <summary>登录堡垒机后按菜单编号选择目标。</summary>
    public const string Bastion = "ssh-bastion";
    /// <summary>SOCKS5 或 HTTP CONNECT 代理路径。</summary>
    public const string Proxy = "ssh-proxy";
    /// <summary>密码认证。</summary>
    public const string Password = "password";
    /// <summary>本机私钥引用认证。</summary>
    public const string PrivateKey = "private-key";
    /// <summary>本机 Agent 认证。</summary>
    public const string Agent = "agent";
}
