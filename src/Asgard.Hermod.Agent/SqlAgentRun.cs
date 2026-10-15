using System.Text.Json;
using Asgard.Hermod.Abstractions.Models;

namespace Asgard.Hermod.Agent;

/// <summary>SQL 自动模式的有界查询闭环；模型只获得只读工具，写入建议仍留给用户确认。</summary>
internal static class SqlAgentRun
{
    private static readonly IReadOnlyList<AgentToolDefinition> Tools = [new("sql_query", "Run one read-only query in the explicitly selected database. Use bounded SELECT, WITH, SHOW, DESCRIBE or EXPLAIN. Never request writes, transaction control, file access or locking. Proposed modifications must instead be explained in fenced sql blocks for user confirmation.", JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { database = new { type = "string", maxLength = 1024 }, sql = new { type = "string", maxLength = 8192 } },
        required = new[] { "database", "sql" }, additionalProperties = false,
    }))];

    /// <summary>工具结果只属于本次任务；最多四次查询，未知结果或拒绝后只允许总结。</summary>
    internal static async Task<AgentReply> RunAsync(IAgentClient client, List<AgentMessage> messages, AgentSqlContext context,
        Func<SqlAgentQuery, CancellationToken, Task<SqlAgentResult>> execute, Action<AgentReply> receive, CancellationToken token)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        bool stopped = false;
        for (int step = 0; step <= 4; step++)
        {
            token.ThrowIfCancellationRequested();
            bool final = stopped || step == 4 || messages.Sum(message => (long)message.Content.Length) > 130000;
            if (final) messages.Add(new(AgentRole.System, "No more queries are authorized for this task. Summarize observed results and uncertainty. Do not claim a rejected, failed, cancelled or unknown query succeeded. Do not retry it."));
            var reply = await client.CompleteWithToolsAsync(messages, final ? [] : Tools, token);
            token.ThrowIfCancellationRequested();
            if (reply.ToolCalls.Count == 0) { receive(reply); return reply; }
            if (final || reply.ToolCalls.Count != 1 || reply.Content.Length > 4000) throw Text.Exception("AgentInvalidResponse");
            var call = reply.ToolCalls[0];
            if (string.IsNullOrWhiteSpace(call.Id) || call.Id.Length > 128 || !ids.Add(call.Id)) throw Text.Exception("AgentInvalidResponse");
            var query = Parse(call, context);
            if (!string.IsNullOrWhiteSpace(reply.Content)) receive(reply);
            messages.Add(new(AgentRole.Assistant, reply.Content) { ToolCalls = reply.ToolCalls });
            var result = await execute(query, token);
            token.ThrowIfCancellationRequested();
            if (result.Output.Length > 8192) throw Text.Exception("AgentInvalidResponse");
            messages.Add(new(AgentRole.Tool, JsonSerializer.Serialize(new { status = result.Status, output = result.Output })) { ToolCallId = call.Id });
            stopped = result.StopActions;
        }
        throw Text.Exception("AgentInvalidResponse");
    }

    /// <summary>执行前验证整个调用，拒绝重复参数、越界目标、写入与未声明工具。</summary>
    private static SqlAgentQuery Parse(AgentToolCall call, AgentSqlContext context)
    {
        try
        {
            if (call.Name != "sql_query" || call.Arguments.Length > 10000) throw Text.Exception("AgentInvalidResponse");
            using var json = JsonDocument.Parse(call.Arguments);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Text.Exception("AgentInvalidResponse");
            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 2 || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != 2
                || properties.Any(property => property.Value.ValueKind != JsonValueKind.String)
                || !root.TryGetProperty("database", out var database) || !root.TryGetProperty("sql", out var sql)) throw Text.Exception("AgentInvalidResponse");
            string target = database.GetString()!, text = sql.GetString()!;
            if (target.Length == 0 || target != context.Database || context.Databases is { } allowed && !allowed.Contains(target, StringComparer.Ordinal))
                throw Text.Exception("DatabaseSessionTargetOutsideScope");
            ReadOnlySqlQuery.Validate(text);
            return new(target, text);
        }
        catch (JsonException) { throw Text.Exception("AgentInvalidResponse"); }
    }
}
