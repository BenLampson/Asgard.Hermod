namespace Asgard.Hermod.Ssh;

/// <summary>为终端与隧道共用连接预处理，将当前设备的私钥文件冻结为可加密同步的内容。</summary>
public static class SshConnectionPreparation
{
    /// <summary>只导入选中认证方式的私钥，保留已导入内容且不修改调用方参数。</summary>
    public static async Task<InstanceConnectionOptions> PrepareAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = SshConnectionOptions.Parse(options);
        var values = new Dictionary<string, string>(options.Parameters, StringComparer.Ordinal);
        foreach (string prefix in profile.Route == SshIds.Jump ? new[] { "", "jump." } : [""])
            if (values.GetValueOrDefault(prefix + "auth") == SshIds.PrivateKey)
                values[prefix + "key"] = await SshPrivateKey.ImportAsync(values[prefix + "key"], cancellationToken).ConfigureAwait(false);
        return new() { TypeId = options.TypeId, ProviderId = options.ProviderId, Parameters = values };
    }
}
