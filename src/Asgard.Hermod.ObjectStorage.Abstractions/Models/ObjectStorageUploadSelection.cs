namespace Asgard.Hermod.ObjectStorage.Abstractions.Models;

/// <summary>上传窗口返回的已确认队列与用户选择的同名处理策略。</summary>
public sealed record ObjectStorageUploadSelection(IReadOnlyList<ObjectStorageUploadItem> Items,
    ObjectStorageOverwritePolicy OverwritePolicy = ObjectStorageOverwritePolicy.Ask);
