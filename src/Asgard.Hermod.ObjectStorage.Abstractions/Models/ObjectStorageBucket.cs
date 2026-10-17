namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>对象存储容器的安全信息；区域用于选择容器自己的服务地址。</summary>
public sealed record ObjectStorageBucket(string Name, string Region, string StorageClass = "Standard");
