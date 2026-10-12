namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>用户选择的本地文件及上传后的对象键；键可保留所选目录的相对结构。</summary>
public sealed record ObjectStorageUploadItem(string LocalPath, string Key, long Size);
