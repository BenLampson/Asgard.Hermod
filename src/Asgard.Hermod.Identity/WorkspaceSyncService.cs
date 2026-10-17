using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.Identity;

/// <summary>只上传密文；会话锁覆盖整个操作，切换账号不能改变下载的本地归属。</summary>
public sealed class WorkspaceSyncService(IAccountService accounts, ILocalWorkspaceStore local,
    IAccountSessionStore settings, IHttpClientFactory clients, HermodIdentityOptions options) : IWorkspaceSyncService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public Task UploadAsync(string password, CancellationToken cancellationToken = default) =>
        accounts.RunAuthenticatedAsync(async (account, token) =>
        {
            WorkspaceEncryption.ValidatePassword(password);
            string owner = Owner(account);
            var snapshot = await local.ExportAsync(cancellationToken);
            var envelope = await Task.Run(() => WorkspaceEncryption.Encrypt(snapshot, password, owner), cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Put, "api/sync/workspace")
            {
                Content = JsonContent.Create(new WorkspaceUpload(settings.LoadRevision(owner), envelope), options: Json),
            };
            var cloud = await SendAsync(request, token, cancellationToken);
            settings.SaveRevision(owner, cloud.Revision);
        }, cancellationToken);

    /// <inheritdoc />
    public Task DownloadAsync(string password, CancellationToken cancellationToken = default) =>
        accounts.RunAuthenticatedAsync(async (account, token) =>
        {
            WorkspaceEncryption.ValidatePassword(password);
            string owner = Owner(account);
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/sync/workspace");
            var cloud = await SendAsync(request, token, cancellationToken);
            if (cloud.Workspace is null) throw Text.Exception("SyncCloudEmpty");
            var snapshot = await Task.Run(() => WorkspaceEncryption.Decrypt(cloud.Workspace, password, owner), cancellationToken);
            await local.RestoreAsync(snapshot, cancellationToken);
            settings.SaveRevision(owner, cloud.Revision);
        }, cancellationToken);

    /// <summary>派生稳定关联键，密码学附加数据和本地版本共享同一归属边界。</summary>
    private string Owner(HermodAccount account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { options.Authority, options.ApplicationId, account.TenantId, account.SubjectId }))));

    /// <summary>只接受成功包络，不能把 SDK 或服务器返回的 message 直接展示。</summary>
    private async Task<CloudWorkspace> SendAsync(HttpRequestMessage request, string token, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var client = clients.CreateClient("hermod.sync");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict) throw Text.Exception("SyncConflict");
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw Text.Exception("SignInAgain");
        if (response.StatusCode == HttpStatusCode.Forbidden) throw Text.Exception("SyncForbidden");
        if (!response.IsSuccessStatusCode) throw Text.Exception("SyncRequestFailed");
        await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, cancellationToken);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!result.RootElement.TryGetProperty("code", out var code) || !code.TryGetInt32(out int value) || value != 200
            || !result.RootElement.TryGetProperty("data", out var data)) throw Text.Exception("SyncRequestFailed");
        var cloud = data.Deserialize<CloudWorkspace>(Json) ?? throw Text.Exception("SyncRequestFailed");
        if (string.IsNullOrWhiteSpace(cloud.Revision) || cloud.Revision.Length > 64) throw Text.Exception("SyncRequestFailed");
        return cloud;
    }
}
