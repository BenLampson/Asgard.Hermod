using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Asgard.Hermod.Providers.S3;

/// <summary>仅对列桶 XML 做受限字段适配，正文上传下载始终流式经过应用连接池。</summary>
internal sealed class S3BucketListResponseHandler(HttpClient transport, S3BucketListDialect dialect) : HttpMessageHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var response = await ForwardAsync(request, token).ConfigureAwait(false);
        if (request.Method != HttpMethod.Get || request.RequestUri?.AbsolutePath != "/" || !response.IsSuccessStatusCode ||
            !request.RequestUri.Query.TrimStart('?').Split('&').Any(part => part.StartsWith(dialect.PageSizeParameter + "=", StringComparison.Ordinal)) ||
            request.RequestUri.Query.Contains("list-type=", StringComparison.Ordinal))
            return response;
        try
        {
            // 列桶每页最多 1000 条，限制 XML 大小且禁用外部实体，避免非预期响应无限占用内存。
            await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, token).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 4 * 1024 * 1024,
            });
            var document = await XDocument.LoadAsync(reader, LoadOptions.None, token).ConfigureAwait(false);
            if (document.Root?.Name.LocalName != "ListAllMyBucketsResult") throw S3Errors.InvalidResponse();
            foreach (var bucket in document.Root.Elements().Where(element => element.Name.LocalName == "Buckets")
                         .SelectMany(element => element.Elements()).Where(element => element.Name.LocalName == "Bucket"))
            {
                var region = bucket.Elements().FirstOrDefault(element => element.Name.LocalName == dialect.RegionElement);
                if (region is not null && !bucket.Elements().Any(element => element.Name.LocalName == "BucketRegion"))
                    region.Name = region.Name.Namespace + "BucketRegion";
            }
            if (dialect.CursorElement != "ContinuationToken")
            {
                var cursor = document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == dialect.CursorElement);
                if (cursor is not null) cursor.Name = cursor.Name.Namespace + "ContinuationToken";
            }
            response.Content.Dispose();
            response.Content = new StringContent(document.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/xml");
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>HttpClient 不允许同一消息发送两次；复制消息壳，正文仍由 SDK 持有和释放。</summary>
    private async Task<HttpResponseMessage> ForwardAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content,
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };
        foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options) forwarded.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        try { return await transport.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); }
        finally { forwarded.Content = null; }
    }

    /// <summary>只释放本实例的 HTTP 包装，池化处理器生命周期仍由应用工厂管理。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) transport.Dispose();
        base.Dispose(disposing);
    }
}
