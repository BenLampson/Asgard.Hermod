namespace Asgard.Hermod.Ssh;

/// <summary>协议模块通过可取消交互获得主机核验结果，不依赖 WPF。</summary>
public interface ISshHostKeyPrompt
{
    /// <summary>仅用户明确核验后返回 true；取消或关闭必须返回 false。</summary>
    Task<bool> ConfirmAsync(SshHostKeyChallenge challenge, CancellationToken cancellationToken);
}
