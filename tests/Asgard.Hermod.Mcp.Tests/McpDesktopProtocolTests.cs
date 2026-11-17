using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpDesktopProtocolTests
{
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task NamedPipeAuthenticatesAndRunsTheSdkProtocol(string version)
    {
        var target = new McpTarget("shared", "orders", "mysql", "v1");
        using var rig = new McpTestRig([target]);
        await rig.GrantAsync(target, "orders");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        string name = "Hermod.Mcp.Tests." + Guid.NewGuid().ToString("N");
        await using var server = new McpDesktopServer(rig.Access, new(rig.Tools), name: name);
        server.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(McpTestRig.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        CancellationToken token = deadline.Token;

        await using (var invalid = await McpPipeEndpoint.ConnectAsync(name, token))
            Assert.False(await McpPipeEndpoint.AuthenticateAsync(invalid, new string('0', 64), token));

        await using var pipe = await McpPipeEndpoint.ConnectAsync(name, token);
        Assert.True(McpPipeEndpoint.IsLocalPeer(pipe, peerIsServer: true));
        Assert.True(await McpPipeEndpoint.AuthenticateAsync(pipe, rig.Session.Policy.Secret, token));
        await using var client = await McpClient.CreateAsync(new StreamClientTransport(pipe, pipe), new() { ProtocolVersion = version }, cancellationToken: token);
        var tools = await client.ListToolsAsync(cancellationToken: token);
        Assert.Contains(tools, tool => tool.Name == "hermod_db_describe_table");
        Assert.DoesNotContain(tools, tool => tool.Name is "hermod_db_read_rows" or "hermod_ssh_diagnose");
        var result = await client.CallToolAsync("hermod_list_instances", cancellationToken: token);
        var row = Assert.Single(result.StructuredContent!.Value.GetProperty("instances").EnumerateArray());
        Assert.Equal("orders", row.GetProperty("scope").GetProperty("database").GetString());
        await rig.Access.SetEnabledAsync("", false, token);
        await using var inactive = await McpPipeEndpoint.ConnectAsync(name, token);
        Assert.Equal(0, await inactive.ReadAsync(new byte[1], token));
    }
}
