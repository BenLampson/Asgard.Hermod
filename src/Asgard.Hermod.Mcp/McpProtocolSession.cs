using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Asgard.Hermod.Mcp;

/// <summary>在已配对流上用官方 SDK 处理协议协商、发现、调用和客户端取消。</summary>
internal sealed class McpProtocolSession(McpTools tools)
{
    internal async Task RunAsync(Stream pipe, McpSessionAccess access, CancellationToken token)
    {
        var cursors = new McpCursorCache();
        await using var server = McpServer.Create(new StreamServerTransport(new McpBoundedInput(pipe), pipe), new()
        {
            ServerInfo = new() { Name = "hermod", Version = typeof(McpProtocolSession).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" },
            InitializationTimeout = TimeSpan.FromSeconds(10),
            ScopeRequests = false,
            ServerInstructions = Text.Get("McpProtocolInstructions"),
            Handlers = new()
            {
                ListToolsHandler = (_, _) =>
                {
                    try { return ValueTask.FromResult(new ListToolsResult { Tools = tools.List(access).ToList() }); }
                    catch (Exception error) { throw new McpException(Text.Error(error, "McpAccessUnavailable")); }
                },
                CallToolHandler = (request, requestToken) => new(tools.InvokeAsync(access, request.Params?.Name ?? "",
                    request.Params?.Arguments?.ToDictionary(StringComparer.Ordinal), cursors, requestToken,
                    request.Params?.ProgressToken is { } progressToken
                        ? (progress, ct) => request.Server.NotifyProgressAsync(progressToken, progress, cancellationToken: ct)
                        : null)),
            },
        });
        await server.RunAsync(token).ConfigureAwait(false);
    }
}
