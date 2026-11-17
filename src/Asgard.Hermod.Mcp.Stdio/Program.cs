using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Mcp;

// CLI 仅传输协议字节；不打开配置数据库、不启动另一个 WPF 进程。
string? secret = Environment.GetEnvironmentVariable(McpPipeEndpoint.SecretVariable);
Environment.SetEnvironmentVariable(McpPipeEndpoint.SecretVariable, null);
if (!McpPipeEndpoint.IsSecret(secret))
{
    Console.Error.WriteLine(Text.Get("McpClientConfigurationRequired"));
    return 1;
}

try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await using var pipe = await McpPipeEndpoint.ConnectAsync(McpPipeEndpoint.Name, timeout.Token).ConfigureAwait(false);
    if (!McpPipeEndpoint.IsLocalPeer(pipe, peerIsServer: true) ||
        !await McpPipeEndpoint.AuthenticateAsync(pipe, secret!, timeout.Token).ConfigureAwait(false))
        throw Text.Exception("McpClientUnavailable");

    using var stdin = Console.OpenStandardInput();
    using var stdout = Console.OpenStandardOutput();
    Task input = stdin.CopyToAsync(pipe);
    Task output = pipe.CopyToAsync(stdout);
    // Windows 的标准输入可能阻塞在同步读取。先返回一个完成的方向，终止时关闭整个会话。
    await await Task.WhenAny(input, output).ConfigureAwait(false);
    await stdout.FlushAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine(Text.Get("McpClientUnavailable"));
    return 1;
}
