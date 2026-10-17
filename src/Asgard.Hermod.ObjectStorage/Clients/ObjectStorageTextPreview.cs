using System.Text;

namespace Asgard.Hermod.ObjectStorage.Clients;

/// <summary>只把限量 UTF-8 文本交给纯文本视图，不执行或渲染远端文档。</summary>
internal static class ObjectStorageTextPreview
{
    /// <summary>单次预览最多读取二百五十六 KiB，服务商忽略 Range 时也不会突破上限。</summary>
    public const int MaximumBytes = 256 * 1024;

    /// <summary>文本扩展名作为缺失 MIME 的补充，二进制内容仍须通过 UTF-8 校验。</summary>
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".yaml", ".yml", ".csv", ".log", ".ini", ".conf",
        ".toml", ".cs", ".js", ".ts", ".css", ".html", ".sql", ".sh", ".ps1", ".svg",
    };

    /// <summary>只读预览失败不抹掉已读元数据，调用方取消仍原样传播。</summary>
    public static async Task<ObjectStorageObjectDetails> ReadAsync(IObjectStorageClient client, string bucket,
        ObjectStorageObjectDetails details, CancellationToken cancellationToken)
    {
        string contentType = details.ContentType.Split(';')[0].Trim();
        if (!contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
            contentType is not "application/json" and not "application/xml" and not "application/javascript" &&
            !Extensions.Contains(Path.GetExtension(details.Key)))
            return details with { PreviewMessage = Text.Get("ThisFileFormatDoesNotSupportPreview") };
        if (details.Size == 0) return details with { PreviewText = "" };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await using ObjectStorageReadResult result = await client.OpenReadAsync(bucket, details.Key, MaximumBytes,
                deadline.Token).ConfigureAwait(false);
            byte[] bytes = new byte[MaximumBytes];
            int read = 0;
            while (read < bytes.Length)
            {
                int count = await result.Body.ReadAsync(bytes.AsMemory(read), deadline.Token).ConfigureAwait(false);
                if (count == 0) break;
                read += count;
            }
            bool truncated = details.Size > read;
            Decoder decoder = new UTF8Encoding(false, true).GetDecoder();
            char[] characters = new char[Encoding.UTF8.GetMaxCharCount(read)];
            int charCount = decoder.GetChars(bytes.AsSpan(0, read), characters, flush: !truncated);
            string text = new string(characters, 0, charCount).TrimStart('\uFEFF');
            if (text.Any(value => char.IsControl(value) && value is not '\r' and not '\n' and not '\t'))
                return details with { PreviewMessage = Text.Get("TheContentIsNotPreviewableUTF8TextDownloadToViewIt") };
            return details with { PreviewText = text, PreviewMessage = truncated ? Text.Get("ShowingOnlyTheFirst256KBDownloadToViewTheCompleteContent") : null };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return details with { PreviewMessage = Text.Get("TextPreviewTimedOutTryAgainLater") }; }
        catch (DecoderFallbackException) { return details with { PreviewMessage = Text.Get("TheContentIsNotPreviewableUTF8TextDownloadToViewIt") }; }
        catch (ObjectStorageException error) { return details with { PreviewMessage = Text.Error(error, "CannotReadTheTextPreviewDownloadToViewIt") }; }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return details with { PreviewMessage = Text.Get("CannotReadTheTextPreviewDownloadToViewIt") };
        }
    }
}
