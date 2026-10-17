using System.Net;
using System.Net.Http.Headers;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>直接返回可控流，不允许 HttpClient 在测试中提前缓冲正文。</summary>
internal sealed class StreamingHttpHandler(params Stream[] streams) : HttpMessageHandler
{
    private readonly Queue<Stream> _streams = new(streams);
    internal string MediaType { get; set; } = "text/event-stream";
    internal string Accept { get; private set; } = "";
    internal string Body { get; private set; } = "";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Accept = request.Headers.Accept.ToString(); Body = await request.Content!.ReadAsStringAsync(token);
        var content = new StreamContent(_streams.Dequeue()); content.Headers.ContentType = new MediaTypeHeaderValue(MediaType);
        return new(HttpStatusCode.OK) { Content = content };
    }
}
