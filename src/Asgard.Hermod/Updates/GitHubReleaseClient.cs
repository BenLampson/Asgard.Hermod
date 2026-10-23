using System.Net.Http;
using System.Text.Json;
using Asgard.Hermod.Abstractions.Services;

namespace Asgard.Hermod.Updates;

/// <summary>读取官方最新稳定发布；正文只供纯文本展示，不解析远程 HTML 或命令。</summary>
public sealed class GitHubReleaseClient(HttpClient client)
{
    /// <summary>读取有界响应并验证发布标识；网络和格式错误交由调用方安全呈现。</summary>
    public async Task<PublishedApplicationRelease> ReadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/BenLampson/Asgard.Hermod/releases/latest");
        request.Headers.UserAgent.ParseAdd("Asgard-Hermod-UpdateCheck/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(256 * 1024, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var release = document.RootElement;
        string? tag = release.GetProperty("tag_name").GetString();
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()
            || tag is null || !Version.TryParse(tag.TrimStart('v'), out var version) || version.Build < 0)
            throw new InvalidDataException("Invalid stable release metadata.");
        // GitHub 使用三段版本，MSIX 使用四段版本，比较前统一补零。
        version = new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
        string notes = release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
        return new(version, notes);
    }
}
