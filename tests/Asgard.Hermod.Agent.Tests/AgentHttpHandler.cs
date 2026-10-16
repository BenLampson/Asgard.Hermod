using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
            Content = new StringContent(ToSse(Response ?? JsonSerializer.Serialize(new
            {
                choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = "Try this.\n```sql\nSELECT 1;\n```" } } },
            })), Encoding.UTF8, "text/event-stream")
        };
    }
    private static string ToSse(string response)
    {
        try
        {
            var root = JsonNode.Parse(response);
            if (root?["choices"] is JsonArray choices)
                foreach (var item in choices.OfType<JsonObject>())
                {
                    item["index"] = 0;
                    var delta = item["message"]?.DeepClone(); item.Remove("message"); item["delta"] = delta;
                    if (delta is JsonObject message && message["tool_calls"] is JsonArray calls)
                        for (int i = 0; i < calls.Count; i++) if (calls[i] is JsonObject call) call["index"] = i;
                }
            response = root?.ToJsonString() ?? response;
        }
        catch (JsonException) { }
        return $"data: {response}\n\ndata: [DONE]\n\n";
    }
}
