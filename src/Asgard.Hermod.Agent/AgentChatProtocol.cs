using System.Text.Json;

namespace Asgard.Hermod.Agent;

/// <summary>只处理 Chat Completions 的工具协议，工具权限由 SQL 或 SSH 工作区另行验证。</summary>
internal static class AgentChatProtocol
{
    /// <summary>文本请求不会注册工具；空工具列表用于预算耗尽后的最终分析。</summary>
    internal static Dictionary<string, object?> Request(string model, IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition>? tools)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(Message).ToArray(),
            ["stream"] = true,
            ["store"] = false,
        };
        if (tools is not null)
        {
            body["tool_choice"] = tools.Count == 0 ? "none" : "auto";
            if (tools.Count > 0)
            {
                body["parallel_tool_calls"] = false;
                body["tools"] = tools.Select(tool => new { type = "function", function = new { name = tool.Name, description = tool.Description, parameters = tool.Parameters } }).ToArray();
            }
        }
        return body;
    }

    /// <summary>保留调用与结果的标识关联，普通文本路径禁止工具角色和工具字段。</summary>
    internal static void Validate(IReadOnlyList<AgentMessage> messages, bool allowTools)
    {
        if (messages.Count is 0 or > 64 || messages.Any(message => message is null || message.Content is null || !Enum.IsDefined(message.Role)) ||
            messages.Sum(message => (long)message.Content.Length + message.ToolCalls.Sum(call => (long)call.Arguments.Length)) > 200_000)
            throw Text.Exception("AgentInputTooLarge");
        if (!allowTools && messages.Any(message => message.Role == AgentRole.Tool || message.ToolCallId is not null || message.ToolCalls.Count > 0))
            throw Text.Exception("AgentInvalidResponse");
    }

    /// <summary>拒绝截断、混合完成状态、重复标识和未经声明的工具，执行前整批验证。</summary>
    internal static AgentReply ReadReply(JsonElement root, IReadOnlyList<AgentToolDefinition>? tools)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw Text.Exception("AgentInvalidResponse");
        var first = choices[0];
        if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("finish_reason", out var finish) || finish.ValueKind != JsonValueKind.String ||
            !first.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            throw Text.Exception("AgentInvalidResponse");
        if (message.TryGetProperty("content", out var rawContent) && rawContent.ValueKind is not JsonValueKind.String and not JsonValueKind.Null) throw Text.Exception("AgentInvalidResponse");
        string content = message.TryGetProperty("content", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        if (content.Length > 64000) throw Text.Exception("AgentInvalidResponse");
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind != JsonValueKind.Null)
        {
            if (calls.ValueKind != JsonValueKind.Array) throw Text.Exception("AgentInvalidResponse");
            if (calls.GetArrayLength() > 0)
            {
                // 串行工具策略不接受并行批次，防止先执行一部分后才发现剩余动作不合法。
                if (tools is null || finish.GetString() != "tool_calls" || calls.GetArrayLength() != 1 || content.Length > 4000)
                    throw Text.Exception("AgentInvalidResponse");
                var call = calls[0];
                if (RequiredString(call, "type") != "function") throw Text.Exception("AgentInvalidResponse");
                string id = RequiredString(call, "id");
                if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object) throw Text.Exception("AgentInvalidResponse");
                string name = RequiredString(function, "name");
                string arguments = RequiredString(function, "arguments");
                if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || arguments is null || arguments.Length > 10000 || !tools.Any(tool => tool.Name == name))
                    throw Text.Exception("AgentInvalidResponse");
                return new(content, []) { ToolCalls = [new(id, name, arguments)] };
            }
        }
        if (finish.GetString() != "stop" || string.IsNullOrWhiteSpace(content)) throw Text.Exception("AgentInvalidResponse");
        return AgentReply.FromContent(content);
    }

    /// <summary>缺失或类型错误的协议字段统一转换为安全资源，避免泄露服务原文。</summary>
    private static string RequiredString(JsonElement node, string name)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw Text.Exception("AgentInvalidResponse");
        return value.GetString()!;
    }

    /// <summary>仅在声明工具后序列化工具关联，普通消息沿用文本格式。</summary>
    private static Dictionary<string, object?> Message(AgentMessage message)
    {
        var result = new Dictionary<string, object?> { ["role"] = message.Role switch { AgentRole.System => "system", AgentRole.User => "user", AgentRole.Tool => "tool", _ => "assistant" }, ["content"] = message.Content };
        if (message.ToolCallId is not null) result["tool_call_id"] = message.ToolCallId;
        if (message.ToolCalls.Count > 0)
            result["tool_calls"] = message.ToolCalls.Select(call => new { id = call.Id, type = "function", function = new { name = call.Name, arguments = call.Arguments } }).ToArray();
        return result;
    }
}
