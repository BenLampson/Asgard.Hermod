using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Asgard.Hermod.Agent;

/// <summary>通过 Chat Completions 请求用户配置的模型；只有 SSH 排障显式注册工具。</summary>
public sealed class OpenAiCompatibleAgentClient(HttpClient client, AgentConfiguration configuration, AgentModelCatalog? catalog = null) : IAgentClient, IAgentSessionClientFactory
{
    private string _profileName = "";
    /// <inheritdoc />
    public AgentModelSession? CreateSession() => catalog?.CreateSession(this);
    /// <inheritdoc />
    public IAgentClient ForOptions(AgentModelOptions options, string profileName)
    {
        var frozen = new AgentConfiguration(); frozen.Update(options.Endpoint, options.Model, options.ApiKey);
        return new OpenAiCompatibleAgentClient(client, frozen) { _profileName = profileName };
    }
    private const int MaximumResponseBytes = 1024 * 1024;
    /// <inheritdoc />
    public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => CompleteCoreAsync(messages, null, cancellationToken);
    /// <inheritdoc />
    public Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default) => CompleteCoreAsync(messages, tools, cancellationToken);

    private async Task<AgentReply> CompleteCoreAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition>? tools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuration.Current;
        var endpoint = options.RequestEndpoint();
        AgentChatProtocol.Validate(messages, tools is not null);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = JsonContent.Create(AgentChatProtocol.Request(options.Model, messages, tools));
            if (options.ApiKey.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Headers.Accept.Add(new("application/json"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw Text.Exception(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "AgentAuthenticationFailed",
                HttpStatusCode.TooManyRequests => "AgentRateLimited",
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity when tools is { Count: > 0 } => "SshAgentToolSupportRequired",
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
            var reply = AgentChatProtocol.ReadReply(document.RootElement, tools);
            cancellationToken.ThrowIfCancellationRequested();
            string model = document.RootElement.TryGetProperty("model", out var returned) && returned.ValueKind == JsonValueKind.String ? returned.GetString() ?? "" : "";
            string identity = model.Length is > 0 and <= 200 && !model.Any(char.IsControl) ? model : options.Model;
            return reply with { ModelIdentity = _profileName.Length == 0 ? identity : Text.Format("AgentModelIdentity", _profileName, identity) };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw Text.Exception("AgentTimedOut"); }
        catch (HttpRequestException) { throw Text.Exception("AgentRequestFailed"); }
        catch (IOException) { throw Text.Exception("AgentRequestFailed"); }
        catch (JsonException) { throw Text.Exception("AgentInvalidResponse"); }
        catch (Exception error) when (error.Data["Hermod.ResourceKey"] is not string) { throw Text.Exception("AgentRequestFailed"); }
    }

}
