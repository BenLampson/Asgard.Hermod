namespace Asgard.Hermod.ObjectStorage.Abstractions.Clients;

/// <summary>适配器返回的只读正文与响应实际长度，释放时关闭远程响应流。</summary>
public sealed record ObjectStorageReadResult(Stream Body, long Size) : IAsyncDisposable
{
    /// <inheritdoc />
    public ValueTask DisposeAsync() => Body.DisposeAsync();
}
