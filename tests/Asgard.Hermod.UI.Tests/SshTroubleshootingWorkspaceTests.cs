using System.Text.Json;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过真实工作区边界验证自动诊断、变更确认和结果反馈，不连接远程主机。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshTroubleshootingWorkspaceTests(WpfTestFixture fixture)
{
    [Fact]
    public Task DiagnosticsRunAutomaticallyAndConfirmedRestartFeedsNextAnalysis() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var dialogs = new Dialogs { Accept = true };
        var client = new Client([Tool("one", "ssh_diagnostic", new { operation = "service_status", target = "nginx" }), Tool("two", "ssh_command", new { command = "systemctl restart nginx" }), AgentReply.FromContent("Verified the result")]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "user@production", dialogs) { Prompt = "diagnose nginx" };
        dialogs.BeforeConfirm = () => { Assert.Single(connection.Commands); Assert.True(model.IsBusy); Assert.False(model.SendCommand.CanExecute(null)); Assert.False(model.ClearCommand.CanExecute(null)); };
        await model.SendCommand.ExecuteAsync();
        Assert.Equal(2, connection.Commands.Count); Assert.Single(dialogs.Previews); Assert.Equal(connection.Commands[1], dialogs.Previews[0]); Assert.Contains("user@production", dialogs.Details[0]);
        var result = client.Requests[2].Last(message => message.Role == AgentRole.Tool); Assert.Equal("two", result.ToolCallId); Assert.Contains("test output", result.Content);
        Assert.Equal(2, model.Messages.Count(message => message.IsTool)); Assert.Equal("Verified the result", model.Messages[^1].Content); Assert.False(model.IsBusy); Assert.Empty(model.Error);
    });

    [Fact]
    public Task DeniedModificationIsReportedToModelWithoutExecution() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var dialogs = new Dialogs();
        var client = new Client([Tool("one", "ssh_command", new { command = "systemctl restart nginx" }), AgentReply.FromContent("Restart was declined")]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", dialogs) { Prompt = "repair" };
        await model.SendCommand.ExecuteAsync(); Assert.Empty(connection.Commands); Assert.Single(dialogs.Previews); Assert.Empty(client.Tools[1]);
        Assert.Contains("denied", client.Requests[1].Single(message => message.Role == AgentRole.Tool).Content);
        Assert.Empty(model.Messages.Single(message => message.IsTool).Commands);
    });

    [Fact]
    public Task ReconnectDuringConfirmationInvalidatesApprovalAndAllOtherEntrypoints() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new Dialogs { Pending = confirmation.Task };
        var client = new Client([Tool("one", "ssh_command", new { command = "systemctl restart nginx" })]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", dialogs) { Prompt = "repair" };
        var sending = model.SendCommand.ExecuteAsync(); Assert.Single(dialogs.Previews);
        var old = new SshAssistantCommand("reboot", 0); Assert.False(model.ExecuteCommand.CanExecute(old)); Assert.False(model.InsertCommand.CanExecute(old));
        await model.ExecuteCommand.ExecuteAsync(old); Assert.Single(dialogs.Previews);
        model.ConnectionChanged(); confirmation.SetResult(true); await sending;
        Assert.Empty(connection.Commands); Assert.Single(client.Requests); Assert.False(model.ExecuteCommand.CanExecute(old));
    });

    [Fact]
    public Task StopCancelsRemoteDiagnosticWithoutAnotherModelRequest() => fixture.RunOffscreenAsync(async () =>
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new SshWorkspaceTestConnection { Handler = async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return new("", 0, false); } };
        var client = new Client([Tool("one", "ssh_diagnostic", new { operation = "containers", target = "" })]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", new Dialogs()) { Prompt = "diagnose" };
        var sending = model.SendCommand.ExecuteAsync(); await started.Task; model.CancelCommand.Execute(null); await sending;
        Assert.Single(client.Requests); Assert.False(model.IsBusy); Assert.NotEmpty(model.Error); Assert.Empty(model.Messages.Single(message => message.IsTool).Commands);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnknownOutcomesDisableToolsAndNeverExposeExceptionDetails(bool truncated) => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection { Handler = (_, _) => truncated ? Task.FromResult(new SshCommandResult("partial output", -1, true)) : throw new IOException("credential-and-sdk-secret") };
        var client = new Client([Tool("one", "ssh_command", new { command = "systemctl restart nginx" }), AgentReply.FromContent("Verify the server before retrying")]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", new Dialogs { Accept = true }) { Prompt = "repair" };
        await model.SendCommand.ExecuteAsync(); Assert.Single(connection.Commands); Assert.Empty(client.Tools[1]);
        string feedback = client.Requests[1].Single(message => message.Role == AgentRole.Tool).Content;
        Assert.Contains("unknown", feedback); Assert.DoesNotContain("credential-and-sdk-secret", feedback);
        Assert.DoesNotContain("credential-and-sdk-secret", string.Join("", model.Messages.Select(message => message.Content)));
    });

    [Fact]
    public Task InvalidDiagnosticArgumentsCannotReachRemoteOrConfirmation() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var dialogs = new Dialogs();
        var client = new Client([Tool("one", "ssh_diagnostic", new { operation = "service_logs", target = "nginx; reboot" })]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", dialogs) { Prompt = "diagnose" };
        await model.SendCommand.ExecuteAsync(); Assert.Empty(connection.Commands); Assert.Empty(dialogs.Previews); Assert.NotEmpty(model.Error);
    });

    [Fact]
    public Task ManualCommandExecutionAlsoReturnsItsResultToModel() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var dialogs = new Dialogs { Accept = true }; var client = new Client([AgentReply.FromContent("Analyzed manual result")]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", dialogs);
        await model.ExecuteCommand.ExecuteAsync(new SshAssistantCommand("uname -a", 0));
        Assert.Single(connection.Commands); Assert.Single(client.Requests); Assert.Contains("test output", client.Requests[0][^1].Content); Assert.Equal("Analyzed manual result", model.Messages[^1].Content);
    });

    [Fact]
    public Task FencedShellTextIsOnlyASuggestionAndAppearsOnce() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var dialogs = new Dialogs(); var client = new Client([AgentReply.FromContent("Review this\n```bash\nreboot\n```")]);
        using var model = new SshAssistantViewModel(client, () => connection, () => "host", dialogs) { Prompt = "diagnose" };
        await model.SendCommand.ExecuteAsync(); Assert.Empty(connection.Commands); Assert.Empty(dialogs.Previews);
        Assert.Equal("reboot", Assert.Single(model.Messages[^1].Commands).Command); Assert.DoesNotContain("reboot", model.Messages[^1].Content);
    });

    internal static AgentReply Tool(string id, string name, object arguments) => new("", []) { ToolCalls = [new(id, name, JsonSerializer.Serialize(arguments))] };
    internal sealed class Client(IReadOnlyList<AgentReply> replies) : IAgentClient
    {
        internal List<AgentMessage[]> Requests { get; } = [];
        internal List<AgentToolDefinition[]> Tools { get; } = [];
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default)
        { Requests.Add(messages.ToArray()); Tools.Add(tools.ToArray()); return Task.FromResult(replies[Requests.Count - 1]); }
    }
    internal sealed class Dialogs : ISshWorkspaceDialogs
    {
        internal bool Accept { get; init; }
        internal Task<bool>? Pending { get; init; }
        internal Action? BeforeConfirm { get; set; }
        internal List<string> Previews { get; } = [];
        internal List<string> Details { get; } = [];
        public Task<bool> ConfirmCloseAsync(string instanceName) => Task.FromResult(false);
        public Task<bool> ConfirmActionAsync(string title, string detail, string preview)
        { Previews.Add(preview); Details.Add(detail); BeforeConfirm?.Invoke(); return Pending ?? Task.FromResult(Accept); }
    }
}
