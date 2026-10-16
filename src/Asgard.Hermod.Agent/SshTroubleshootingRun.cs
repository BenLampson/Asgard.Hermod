using System.Text.Json;

namespace Asgard.Hermod.Agent;

/// <summary>有限步数的 SSH 工具闭环；执行委托由工作区提供，协议模块不持有连接。</summary>
internal static class SshTroubleshootingRun
{
    internal const int MaximumSteps = 8;

    /// <summary>完整保留当前任务的工具关联，任务结束后由外层只保存用户请求和最终分析。</summary>
    internal static async Task<AgentReply> RunAsync(IAgentClient client, List<AgentMessage> messages,
        Func<SshAgentAction, CancellationToken, Task<SshAgentToolResult>> execute, Action<AgentReply> receive,
        Action ensureCurrent, CancellationToken token, bool allowActions)
    {
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        bool stopped = !allowActions;
        for (int step = 0; step <= MaximumSteps; step++)
        {
            ensureCurrent();
            bool final = stopped || step == MaximumSteps || messages.Sum(message => (long)message.Content.Length + message.ToolCalls.Sum(call => (long)call.Arguments.Length)) > 130000;
            if (final) messages.Add(new(AgentRole.System, "No more tools are authorized for this task. Summarize observed results, uncertainty and next steps. Never claim an unknown or denied action succeeded. Do not retry it."));
            var reply = await client.CompleteWithToolsAsync(messages, final ? [] : SshAgentTools.Definitions, token).WaitAsync(token);
            ensureCurrent();
            if (reply.ToolCalls.Count == 0)
            {
                receive(reply);
                return reply;
            }
            if (final || reply.ToolCalls.Count != 1 || reply.Content.Length > 4000) throw Text.Exception("AgentInvalidResponse");
            var call = reply.ToolCalls[0];
            if (string.IsNullOrWhiteSpace(call.Id) || call.Id.Length > 128 || !callIds.Add(call.Id)) throw Text.Exception("AgentInvalidResponse");
            // 模型输出的所有参数在显示执行前校验；自由文本代码块从不进入此通道。
            var action = SshAgentTools.Parse(call);
            if (!string.IsNullOrWhiteSpace(reply.Content)) receive(reply);
            messages.Add(new(AgentRole.Assistant, reply.Content) { ToolCalls = reply.ToolCalls });
            var result = await execute(action, token);
            ensureCurrent();
            bool truncated = result.Truncated || result.Output.Length > 4000;
            messages.Add(new(AgentRole.Tool, JsonSerializer.Serialize(new
            {
                status = result.Status,
                workingDirectory = "/",
                exitCode = result.ExitCode,
                output = result.Output.Length > 4000 ? result.Output[..4000] : result.Output,
                truncated,
            }))
            { ToolCallId = call.Id });
            stopped = result.StopActions;
        }
        throw Text.Exception("AgentInvalidResponse");
    }
}
