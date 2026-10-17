namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>传输已经完成的字节数；进度以服务返回的总大小为准。</summary>
public sealed record ObjectStorageTransferProgress(long TransferredBytes, long TotalBytes);
