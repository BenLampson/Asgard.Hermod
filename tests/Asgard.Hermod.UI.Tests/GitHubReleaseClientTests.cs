using System.Net;
using System.Net.Http;
using Asgard.Hermod.Updates;

namespace Asgard.Hermod.UI.Tests;

/// <summary>离线验证远程元数据边界及三段发布版本与四段安装版本的比较。</summary>
public sealed class GitHubReleaseClientTests
{
    [Theory]
    [InlineData("v1.0.0", false)]
    [InlineData("v1.0.1", true)]
    [InlineData("v0.9.9", false)]
    [InlineData("v1.0.0.1", true)]
    public async Task Read_NormalizesStableVersion(string tag, bool newer)
    {
        using var handler = new ReleaseHandler($$"""{"tag_name":"{{tag}}","draft":false,"prerelease":false,"body":"# Changes\n<script>untrusted</script>"}""");
        using var client = new HttpClient(handler);
        var release = await new GitHubReleaseClient(client).ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(newer, release.Version > new Version(1, 0, 0, 0));
        Assert.Equal("# Changes\n<script>untrusted</script>", release.Notes);
        Assert.Equal("https://api.github.com/repos/BenLampson/Asgard.Hermod/releases/latest", handler.Address);
        Assert.True(handler.HasUserAgent);
    }

    [Theory]
    [InlineData("{\"tag_name\":\"v2.0.0-beta\",\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v2.0.0\",\"draft\":true,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v2.0.0\",\"draft\":false,\"prerelease\":true}")]
    public async Task Read_RejectsUnpublishedAndInvalidVersions(string json)
    {
        using var handler = new ReleaseHandler(json);
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubReleaseClient(client).ReadAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(429, false)]
    [InlineData(200, true)]
    public async Task Read_RejectsRateLimitAndOversizedResponses(int status, bool oversized)
    {
        using var handler = new ReleaseHandler(oversized ? new string('x', 300 * 1024) : "{}", (HttpStatusCode)status);
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => new GitHubReleaseClient(client).ReadAsync(TestContext.Current.CancellationToken));
    }

    private sealed class ReleaseHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Address { get; private set; }
        public bool HasUserAgent { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Address = request.RequestUri!.AbsoluteUri;
            HasUserAgent = request.Headers.UserAgent.Count > 0;
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
