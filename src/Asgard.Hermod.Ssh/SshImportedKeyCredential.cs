using Tmds.Ssh;

namespace Asgard.Hermod.Ssh;

/// <summary>通过协议库的受保护加载扩展点解析内存私钥，并立即清空临时文本。</summary>
internal sealed class SshImportedKeyCredential(SshHostOptions host) : PrivateKeyCredential(_ => Load(host), "Hermod")
{
    private static ValueTask<Key> Load(SshHostOptions host)
    {
        char[] chars = InstanceFileContent.Decode(host.KeyPath);
        // 非提示模式会同步解析私钥；返回的 Key 不再引用原始文本。
        try { return ValueTask.FromResult(new Key(chars, host.Passphrase)); }
        finally { Array.Clear(chars); }
    }
}
