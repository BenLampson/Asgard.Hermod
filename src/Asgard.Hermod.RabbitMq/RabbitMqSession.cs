using System.Net;
using System.Net.Http.Headers;

namespace Asgard.Hermod.RabbitMq;

/// <summary>有界 Management API 会话，不自动重试任何请求，也不把响应正文作为错误展示。</summary>
public sealed partial class RabbitMqSession : IRabbitMqSession
{
    private readonly HttpClient _client;
    private readonly RabbitMqTransport? _transport;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private string[] _tags = [];
    /// <summary>可注入受控 HTTP 客户端进行离线协议验证。</summary>
    public RabbitMqSession(RabbitMqSessionOptions options, HttpClient client) { Options = options; _client = client; }
    internal RabbitMqSession(RabbitMqSessionOptions options, HttpClient client, RabbitMqTransport transport) : this(options, client) => _transport = transport;
    /// <inheritdoc />
    public RabbitMqSessionOptions Options { get; }
    /// <inheritdoc />
    public RabbitMqRecord Identity { get; private set; } = new(JsonSerializer.SerializeToElement(new { }));
    /// <inheritdoc />
    public string Version { get; private set; } = "";
    /// <inheritdoc />
    public bool IsConnected { get; private set; } = true;
    /// <inheritdoc />
    public bool IsAdministrator => _tags.Contains("administrator", StringComparer.Ordinal);
    /// <inheritdoc />
    public bool CanManagePolicies => IsAdministrator || _tags.Contains("policymaker", StringComparer.Ordinal);
    /// <inheritdoc />
    public bool CanMonitor => IsAdministrator || _tags.Contains("monitoring", StringComparer.Ordinal);
    /// <summary>读取服务器身份与版本，不依赖客户端 UI 标签判断能力。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var identity = new RabbitMqRecord(await RequestAsync(HttpMethod.Get, "whoami", default, false, cancellationToken).ConfigureAwait(false));
        Identity = identity;
        var tags = identity.Value("tags");
        _tags = tags.ValueKind == JsonValueKind.Array ? tags.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
            : identity.String("tags").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var overview = new RabbitMqRecord(await RequestAsync(HttpMethod.Get, "overview", default, false, cancellationToken).ConfigureAwait(false));
        Version = overview.String("rabbitmq_version");
    }

    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, JsonElement body, bool mutates, CancellationToken token, string? reason = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); token.ThrowIfCancellationRequested();
        if (!IsConnected) throw new RabbitMqFailure("RabbitMqDisconnected", connectionLost: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Options.TimeoutSeconds));
        using var request = new HttpRequestMessage(method, new Uri(Options.BaseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Options.Value("user") + ":" + Options.Value("password"))));
        request.Headers.Accept.Add(new("application/json"));
        if (reason is not null) request.Headers.TryAddWithoutValidation("X-Reason", reason);
        if (_transport is not null) request.Options.Set(RabbitMqTransport.Key, _transport);
        if (body.ValueKind != JsonValueKind.Undefined) request.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string key = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "RabbitMqAuthenticationFailed",
                    HttpStatusCode.Forbidden => "RabbitMqPermissionDenied",
                    HttpStatusCode.NotFound => "RabbitMqNotFound",
                    HttpStatusCode.BadRequest or HttpStatusCode.Conflict => "RabbitMqRequestRejected",
                    _ => "RabbitMqRequestFailed"
                };
                bool unknown = mutates && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout);
                bool authenticationLost = response.StatusCode == HttpStatusCode.Unauthorized;
                if (authenticationLost) IsConnected = false;
                throw new RabbitMqFailure(unknown ? "RabbitMqOutcomeUnknown" : key, unknown, authenticationLost);
            }
            if (response.StatusCode == HttpStatusCode.NoContent) return default;
            const int maximum = 12 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximum) throw new RabbitMqFailure(mutates ? "RabbitMqOutcomeUnknown" : "RabbitMqResponseTooLarge", mutates);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); byte[] bytes = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(bytes, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > maximum) throw new RabbitMqFailure(mutates ? "RabbitMqOutcomeUnknown" : "RabbitMqResponseTooLarge", mutates);
                buffer.Write(bytes, 0, read);
            }
            if (buffer.Length == 0) return default;
            using var document = JsonDocument.Parse(buffer.ToArray()); return document.RootElement.Clone();
        }
        catch (RabbitMqFailure) { throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !mutates) { throw; }
        catch (JsonException) { throw new RabbitMqFailure(mutates ? "RabbitMqOutcomeUnknown" : "RabbitMqInvalidResponse", mutates); }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
        {
            IsConnected = false;
            throw new RabbitMqFailure(mutates ? "RabbitMqOutcomeUnknown" : "RabbitMqConnectionFailed", mutates, true);
        }
    }
    internal static string Segment(string value) => value is "." or ".." ? throw Text.Exception("RabbitMqNameInvalid") : Uri.EscapeDataString(value);
    internal static string Exchange(string value) => Segment(value.Length == 0 ? "amq.default" : value);
    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true; IsConnected = false; _lifetime.Cancel(); _client.Dispose(); _transport?.Dispose(); _lifetime.Dispose(); return ValueTask.CompletedTask;
    }
}
