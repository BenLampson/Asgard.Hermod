using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.Agent;

/// <summary>通过 Chat Completions 请求用户配置的模型；只有 SSH 排障显式注册工具。</summary>
public sealed class OpenAiCompatibleAgentClient(HttpClient client, AgentConfiguration configuration, AgentModelCatalog? catalog = null) : IAgentClient, IAgentSessionClientFactory
{
    private string _profileName = "";
    // 超时使用可替换时钟，测试无需等待实际分钟数。
    internal TimeProvider Clock { get; init; } = TimeProvider.System;
    /// <inheritdoc />
    public AgentModelSession? CreateSession() => catalog?.CreateSession(this);
    /// <inheritdoc />
    public IAgentClient ForOptions(AgentModelOptions options, string profileName)
    {
        var frozen = new AgentConfiguration(); frozen.Update(options.Endpoint, options.Model, options.ApiKey);
        return new OpenAiCompatibleAgentClient(client, frozen) { _profileName = profileName, Clock = Clock };
    }
    /// <inheritdoc />
    public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => CompleteStreamingAsync(messages, null, null, cancellationToken);
    /// <inheritdoc />
    public Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default) => CompleteStreamingAsync(messages, tools, null, cancellationToken);

    /// <inheritdoc />
    public async Task<AgentReply> CompleteStreamingAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition>? tools,
        Func<AgentStreamUpdate, CancellationToken, ValueTask>? receive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuration.Current;
        var endpoint = options.RequestEndpoint();
        AgentChatProtocol.Validate(messages, tools is not null);
        try
        {
            using var totalLimit = new CancellationTokenSource(TimeSpan.FromMinutes(15), Clock);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, totalLimit.Token);
            using var headerLimit = new CancellationTokenSource(AgentSseReader.IdleTimeout, Clock);
            using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, headerLimit.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = JsonContent.Create(AgentChatProtocol.Request(options.Model, messages, tools));
            if (options.ApiKey.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            request.Headers.Accept.Add(new("text/event-stream"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token).ConfigureAwait(false);
            headerLimit.Dispose();
            if (!response.IsSuccessStatusCode) throw Text.Exception(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "AgentAuthenticationFailed",
                HttpStatusCode.Forbidden => "AgentAccessForbidden",
                HttpStatusCode.TooManyRequests => "AgentRateLimited",
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity when tools is { Count: > 0 } => "SshAgentToolSupportRequired",
                _ => "AgentRequestFailed",
            });
            // 无论 Content-Length 是否可信，都逐块限制读取，避免模型服务耗尽本地内存。
            if (response.Content.Headers.ContentLength > AgentSseReader.MaximumBytes ||
                !string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                throw Text.Exception("AgentInvalidResponse");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var accumulator = new AgentStreamAccumulator();
            bool done = false;
            await foreach (string data in AgentSseReader.ReadAsync(stream, timeout.Token, Clock).ConfigureAwait(false))
            {
                if (data.Length == 0) continue;
                if (data == "[DONE]") { done = true; break; }
                using var document = JsonDocument.Parse(data);
                if (accumulator.Append(document.RootElement) && receive is not null)
                    await receive(accumulator.Snapshot, timeout.Token).ConfigureAwait(false);
            }
            timeout.Token.ThrowIfCancellationRequested();
            if (!done) throw Text.Exception("AgentInvalidResponse");
            var reply = accumulator.Complete(tools);
            string identity = accumulator.Model.Length > 0 ? accumulator.Model : options.Model;
            return reply with { ModelIdentity = _profileName.Length == 0 ? identity : Text.Format("AgentModelIdentity", _profileName, identity) };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw Text.Exception("AgentTimedOut"); }
        catch (HttpRequestException) { throw Text.Exception("AgentRequestFailed"); }
        catch (IOException) { throw Text.Exception("AgentRequestFailed"); }
        catch (JsonException) { throw Text.Exception("AgentInvalidResponse"); }
        catch (DecoderFallbackException) { throw Text.Exception("AgentInvalidResponse"); }
        catch (Exception error) when (error.Data["Hermod.ResourceKey"] is not string) { throw Text.Exception("AgentRequestFailed"); }
    }

}
