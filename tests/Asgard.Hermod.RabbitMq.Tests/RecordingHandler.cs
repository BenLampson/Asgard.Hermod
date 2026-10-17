namespace Asgard.Hermod.RabbitMq.Tests;

internal sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    internal List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
        return await respond(request, cancellationToken);
    }
    internal static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    internal static InstanceConnectionOptions Connection(string route = RabbitMqIds.Direct, params (string Key, string Value)[] values) => new()
    {
        TypeId = RabbitMqIds.Type,
        ProviderId = route,
        Parameters = new Dictionary<string, string>(new[] { KeyValuePair.Create("host", "mq.example.test"), KeyValuePair.Create("user", "operator"), KeyValuePair.Create("password", "test-password") }
            .Concat(values.Select(item => KeyValuePair.Create(item.Key, item.Value))).GroupBy(item => item.Key).Select(group => group.Last())),
    };
    internal RabbitMqSession Session() => new(RabbitMqSessionOptions.Parse(Connection()), new HttpClient(this));
}
