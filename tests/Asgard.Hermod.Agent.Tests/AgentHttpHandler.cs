using System.Net;
using System.Text.Json;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>仅在内存记录请求的模型边界，测试不访问外部服务。</summary>
internal sealed class AgentHttpHandler : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];
    public List<string?> Keys { get; } = [];
    public List<Uri?> Endpoints { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string? Response { get; set; }
    public Func<CancellationToken, Task>? BeforeResponse { get; set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
        Keys.Add(request.Headers.Authorization?.Parameter); Endpoints.Add(request.RequestUri);
        if (BeforeResponse is not null) await BeforeResponse(cancellationToken);
        return new(Status)
        {
            Content = new StringContent(Response ?? JsonSerializer.Serialize(new
            {
                choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = "Try this.\n```sql\nSELECT 1;\n```" } } },
            }))
        };
    }
}
