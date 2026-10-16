namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>一个仅监听本机回环地址的端口转发，释放句柄停止监听。</summary>
public sealed class SshForwardItem(string label, IDisposable handle, int localPort = 0, string host = "", int remotePort = 0, string name = "") : IDisposable
{
    /// <summary>公开端点，不含凭据。</summary>
    public string Label { get; } = label;
    /// <summary>用户名称或包含真实端口的完整默认模板。</summary>
    public string Name => name.Length > 0 ? name : Text.Format("SshForwardRuleName", localPort);
    /// <summary>本机只监听回环地址，数字保持 invariant。</summary>
    public string LocalEndpoint => localPort > 0 ? "127.0.0.1:" + localPort.ToString(System.Globalization.CultureInfo.InvariantCulture) : Label;
    /// <summary>服务器目标地址，不含凭据。</summary>
    public string RemoteEndpoint => host.Length > 0 ? host + ":" + remotePort.ToString(System.Globalization.CultureInfo.InvariantCulture) : Label;
    /// <inheritdoc />
    public void Dispose() => handle.Dispose();
}
