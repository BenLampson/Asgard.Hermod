using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.Agent;

/// <summary>只汇总索引 0 的串行响应，完整结束后复用工具权限与长度验证。</summary>
internal sealed class AgentStreamAccumulator
{
    private readonly StringBuilder _content = new(), _thinking = new(), _id = new(), _name = new(), _arguments = new();
    private readonly Stopwatch _elapsed = new();
    private string? _finish, _toolType;
    private bool _hasTool;
    internal string Model { get; private set; } = "";
    internal AgentStreamUpdate Snapshot => new(_content.ToString(), _thinking.ToString(), _elapsed.IsRunning, _elapsed.Elapsed);

    internal bool Append(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _) ||
            !root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) Invalid();
        choices = root.GetProperty("choices");
        if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
        {
            string value = model.GetString()!;
            if (value.Length is > 0 and <= 200 && !value.Any(char.IsControl)) Model = value;
        }
        if (choices.GetArrayLength() == 0) return false; // 最后的 usage 事件不包含 choice。
        if (choices.GetArrayLength() != 1 || _finish is not null) Invalid();
        var choice = choices[0];
        if (choice.ValueKind != JsonValueKind.Object || !choice.TryGetProperty("index", out var index) || index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out int number) || number != 0 ||
            !choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) Invalid();
        delta = choice.GetProperty("delta");
        bool changed = false;
        foreach (string key in new[] { "reasoning_content", "reasoning", "thinking" })
        {
            if (!delta.TryGetProperty(key, out var reasoning) || reasoning.ValueKind == JsonValueKind.Null) continue;
            if (reasoning.ValueKind != JsonValueKind.String) Invalid();
            string value = reasoning.GetString()!;
            if (value.Length > 0) { AppendBounded(_thinking, value, 64000); _elapsed.Start(); changed = true; }
            break;
        }
        string content = OptionalString(delta, "content");
        if (content.Length > 0) { AppendBounded(_content, content, 64000); _elapsed.Stop(); changed = true; }
        if (delta.TryGetProperty("tool_calls", out var calls) && calls.ValueKind != JsonValueKind.Null)
        {
            if (calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() > 1) Invalid();
            foreach (var call in calls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object || !call.TryGetProperty("index", out var toolIndex) || toolIndex.ValueKind != JsonValueKind.Number || !toolIndex.TryGetInt32(out int toolNumber) || toolNumber != 0) Invalid();
                _hasTool = true; changed |= _elapsed.IsRunning; _elapsed.Stop();
                AppendBounded(_id, OptionalString(call, "id"), 128);
                string type = OptionalString(call, "type");
                if (type.Length > 0) { if (type != "function") Invalid(); _toolType = type; }
                if (call.TryGetProperty("function", out var function))
                {
                    if (function.ValueKind != JsonValueKind.Object) Invalid();
                    AppendBounded(_name, OptionalString(function, "name"), 128);
                    AppendBounded(_arguments, OptionalString(function, "arguments"), 10000);
                }
            }
        }
        if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind != JsonValueKind.Null)
        {
            if (finish.ValueKind != JsonValueKind.String) Invalid();
            _finish = finish.GetString(); changed |= _elapsed.IsRunning; _elapsed.Stop();
        }
        return changed;
    }

    internal AgentReply Complete(IReadOnlyList<AgentToolDefinition>? tools)
    {
        var message = new Dictionary<string, object?> { ["content"] = _content.ToString() };
        if (_hasTool) message["tool_calls"] = new[] { new { id = _id.ToString(), type = _toolType, function = new { name = _name.ToString(), arguments = _arguments.ToString() } } };
        var root = JsonSerializer.SerializeToElement(new { choices = new[] { new { finish_reason = _finish, message } } });
        return AgentChatProtocol.ReadReply(root, tools) with { Thinking = _thinking.ToString(), ThinkingElapsed = _elapsed.Elapsed };
    }

    private static string OptionalString(JsonElement node, string key)
    {
        if (!node.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return "";
        if (value.ValueKind != JsonValueKind.String) Invalid();
        return value.GetString()!;
    }
    private static void AppendBounded(StringBuilder target, string value, int limit)
    {
        if (target.Length + value.Length > limit) Invalid();
        target.Append(value);
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw Text.Exception("AgentInvalidResponse");
}
