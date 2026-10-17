using System.Net;
using System.Net.Http.Json;

namespace Asgard.Hermod.Identity;

/// <summary>桌面仅调用 Hermod 注册 API，不持有 Heimdall 服务凭据，不保存注册密码。</summary>
public sealed class AccountRegistrationService(IHttpClientFactory clients) : IAccountRegistrationService
{
    /// <inheritdoc />
    public async Task<AccountRegistrationResult> RegisterAsync(AccountRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        try
        {
            using var client = clients.CreateClient("hermod.sync");
            using var message = new HttpRequestMessage(HttpMethod.Post, "api/account/register") { Content = JsonContent.Create(request) };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw Text.Exception(response.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge => "RegistrationInvalidRequest",
                HttpStatusCode.Conflict => "RegistrationConflict",
                HttpStatusCode.TooManyRequests => "RegistrationRateLimited",
                HttpStatusCode.ServiceUnavailable => "RegistrationUnavailable",
                _ => "RegistrationOutcomeUnknown",
            });
            await response.Content.LoadIntoBufferAsync(8192, cancellationToken);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!json.TryGetProperty("code", out var code) || !code.TryGetInt32(out int status) || status != 200
                || !json.TryGetProperty("data", out var data)
                || data.Deserialize<AccountRegistrationResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) is not { } result
                || result.Username != request.Username.Trim()) throw Text.Exception("RegistrationOutcomeUnknown");
            return result;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException
            && error.Data["Hermod.ResourceKey"] is null || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw Text.Exception("RegistrationOutcomeUnknown");
        }
    }
}
