namespace Asgard.Hermod.Ssh;

/// <summary>单个 SSH 主机的私有连接状态；不会默认输出凭据。</summary>
public sealed class SshHostOptions
{
    /// <summary>主机名或 IP。</summary>
    public required string Host { get; init; }
    /// <summary>TCP 端口。</summary>
    public int Port { get; init; } = 22;
    /// <summary>远程用户名。</summary>
    public required string User { get; init; }
    /// <summary>稳定认证标识。</summary>
    public required string Authentication { get; init; }
    /// <summary>密码仅在建立连接时交给协议库。</summary>
    public string Password { get; init; } = "";
    /// <summary>可移植私钥编码，兼容尚未重新保存的本机文件引用。</summary>
    public string KeyPath { get; init; } = "";
    /// <summary>私钥解密口令。</summary>
    public string Passphrase { get; init; } = "";
    /// <summary>诊断文本不能输出凭据。</summary>
    public override string ToString() => "SSH host (credentials hidden)";
}
