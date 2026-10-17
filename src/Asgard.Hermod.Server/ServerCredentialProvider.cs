namespace Asgard.Hermod.Server;

/// <summary>读取服务器安全配置的独立服务凭据，不写入同步数据库。</summary>
public sealed class ServerCredentialProvider(IOptionsMonitor<HermodServerOptions> options) : IHeimdallCredentialProvider
{
    /// <inheritdoc />
    public ValueTask<HeimdallClientCredentials> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (name is not ("hermod-directory" or "hermod-registration")) throw new ArgumentOutOfRangeException(nameof(name));
        var value = options.CurrentValue;
        return ValueTask.FromResult(new HeimdallClientCredentials(value.ServiceClientId, value.ServiceClientSecret, value.ServiceCredentialVersion));
    }
}
