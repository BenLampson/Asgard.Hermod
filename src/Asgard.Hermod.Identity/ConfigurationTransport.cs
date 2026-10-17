using System.Net;
using System.Net.Http.Json;

namespace Asgard.Hermod.Identity;

/// <summary>只解析成功响应和稳定错误码，不显示服务器原始消息。</summary>
internal sealed class ConfigurationTransport(IHttpClientFactory clients)
{
    /// <summary>读取当前账户的配置明细。</summary>
    public Task<CloudConfiguration> ReadAsync(string token, CancellationToken cancellationToken) => SendAsync(HttpMethod.Get, token, null, cancellationToken);
    /// <summary>版本匹配才提交合并后的配置集合。</summary>
    public Task<CloudConfiguration> WriteAsync(string token, ConfigurationUpload upload, CancellationToken cancellationToken) => SendAsync(HttpMethod.Put, token, upload, cancellationToken);
    /// <summary>限制响应体，按稳定错误码反馈。</summary>
    private async Task<CloudConfiguration> SendAsync(HttpMethod method, string token, ConfigurationUpload? upload, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient("hermod.sync");
        using var request = new HttpRequestMessage(method, "api/sync/configuration");
        request.Headers.Authorization = new("Bearer", token);
        if (upload is not null) request.Content = JsonContent.Create(upload);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict) throw Text.Exception("SyncConflict");
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw Text.Exception("SignInAgain");
        if (response.StatusCode == HttpStatusCode.Forbidden) throw Text.Exception("SyncForbidden");
        if (!response.IsSuccessStatusCode) throw Text.Exception("SyncRequestFailed");
        await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, cancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (!json.TryGetProperty("code", out var code) || !code.TryGetInt32(out int result) || result != 200
            || !json.TryGetProperty("data", out var data)) throw Text.Exception("SyncRequestFailed");
        var cloud = data.Deserialize<CloudConfiguration>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (cloud is null || string.IsNullOrWhiteSpace(cloud.Revision) || cloud.Revision.Length > 64
            || cloud.Instances is null || cloud.Tags is null || cloud.Instances.Count > 10000 || cloud.Tags.Count > 10000)
            throw Text.Exception("SyncSnapshotInvalid");
        return cloud;
    }
}
