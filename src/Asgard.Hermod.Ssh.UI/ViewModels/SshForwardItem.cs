namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>一个仅监听本机回环地址的端口转发，释放句柄停止监听。</summary>
public sealed class SshForwardItem(string label, IDisposable handle) : IDisposable
{
    /// <summary>公开端点，不含凭据。</summary>
    public string Label { get; } = label;
    /// <inheritdoc />
    public void Dispose() => handle.Dispose();
}
