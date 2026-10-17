using System.Diagnostics;

namespace Asgard.Hermod.Identity;

/// <summary>只有预先配置的 HTTPS 发行者响应可以影响登录验签时钟。</summary>
public sealed class HeimdallClockHandler(HeimdallServerClock clock, HermodIdentityOptions options) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        var response = await base.SendAsync(request, cancellationToken);
        var trusted = new Uri(options.Authority);
        if (request.RequestUri is { Scheme: "https" } uri && uri.Host == trusted.Host && uri.Port == trusted.Port
            && response.IsSuccessStatusCode && response.Headers.Date is { } date)
        {
            try { clock.Observe(date, Stopwatch.GetElapsedTime(started)); }
            catch { response.Dispose(); throw; }
        }
        return response;
    }
}
