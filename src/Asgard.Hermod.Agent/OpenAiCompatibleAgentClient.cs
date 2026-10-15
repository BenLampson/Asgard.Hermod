using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Asgard.Hermod.Agent;

/// <summary>通过 Chat Completions 文本协议请求用户配置的模型，不注册任何工具或数据库连接。</summary>
public sealed class OpenAiCompatibleAgentClient(HttpClient client, AgentConfiguration configuration) : IAgentClient
{
    private const int MaximumResponseBytes = 1024 * 1024;
    /// <inheritdoc />
    public async Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuration.Current;
        var endpoint = options.RequestEndpoint();
        if (messages.Count is 0 or > 64 || messages.Any(message => message is null || message.Content is null || !Enum.IsDefined(message.Role)) ||
            messages.Sum(message => (long)message.Content.Length) > 200_000) throw Text.Exception("AgentInputTooLarge");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = JsonContent.Create(new
            {
                model = options.Model,
                messages = messages.Select(message => new { role = Role(message.Role), content = message.Content }),
                stream = false,
                store = false,
            });
            if (options.ApiKey.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Headers.Accept.Add(new("application/json"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw Text.Exception(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "AgentAuthenticationFailed",
                HttpStatusCode.TooManyRequests => "AgentRateLimited",
                _ => "AgentRequestFailed",
            });
            // 无论 Content-Length 是否可信，都逐块限制读取，避免模型服务耗尽本地内存。
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw Text.Exception("AgentInvalidResponse");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var body = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                int read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                if (body.Length + read > MaximumResponseBytes) throw Text.Exception("AgentInvalidResponse");
                body.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length));
            string content = ReadContent(document.RootElement);
            cancellationToken.ThrowIfCancellationRequested();
            return AgentReply.FromContent(content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw Text.Exception("AgentTimedOut"); }
        catch (HttpRequestException) { throw Text.Exception("AgentRequestFailed"); }
        catch (IOException) { throw Text.Exception("AgentRequestFailed"); }
        catch (JsonException) { throw Text.Exception("AgentInvalidResponse"); }
        catch (Exception error) when (error.Data["Hermod.ResourceKey"] is not string) { throw Text.Exception("AgentRequestFailed"); }
    }

    private static string Role(AgentRole role) => role switch { AgentRole.System => "system", AgentRole.User => "user", _ => "assistant" };

    private static string ReadContent(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object) throw Text.Exception("AgentInvalidResponse");
        var first = choices[0];
        // 截断的代码块不可作为完整建议；工具调用也没有在本模块中获得执行权限。
        if (!first.TryGetProperty("finish_reason", out var finish) || finish.ValueKind != JsonValueKind.String || finish.GetString() != "stop" ||
            !first.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            throw Text.Exception("AgentInvalidResponse");
        string text = content.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64_000) throw Text.Exception("AgentInvalidResponse");
        return text;
    }
}
