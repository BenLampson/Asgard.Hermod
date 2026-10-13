using Asgard.Hermod.ObjectStorage.UI.Models;

namespace Asgard.Hermod.ObjectStorage.UI.Services;

/// <summary>封装对象操作的窗体、文件选择与系统剪贴板，便于替换与验证。</summary>
public interface IObjectStorageDialogService
{
    /// <summary>选择文件或目录，确认上传队列及覆盖策略。</summary>
    Task<ObjectStorageUploadSelection?> SelectUploadAsync(ObjectStorageBucket bucket, string prefix);

    /// <summary>选择单个对象下载位置；取消时返回空值。</summary>
    Task<string?> SelectDownloadPathAsync(string suggestedName);

    /// <summary>选择批量下载目录；对象键不直接作为本地绝对路径。</summary>
    Task<string?> SelectDownloadFolderAsync();

    /// <summary>读取通过验证的当前目录名称。</summary>
    Task<string?> RequestFolderNameAsync(ObjectStorageBucket bucket, string prefix);

    /// <summary>展示完整对象键范围，并要求用户明确确认删除。</summary>
    Task<bool> ConfirmDeleteAsync(ObjectStorageBucket bucket, string prefix, ObjectStorageDeletePlan plan);

    /// <summary>默认策略下对同名对象确认覆盖。</summary>
    Task<bool> ConfirmOverwriteAsync(string key);

    /// <summary>让用户选择有效期、生成并复制链接；生成回调只在用户操作后调用。</summary>
    Task ShowTemporaryLinkAsync(string key, Func<TimeSpan, Task<string>> generate);

    /// <summary>确认预览类型与流量费用；取消时返回空值，窗体不下载对象。</summary>
    Task<ObjectStoragePreviewType?> RequestForcePreviewAsync(string name, long size, ObjectStoragePreviewType? inferredType);

    /// <summary>仅在用户点击复制时写入系统剪贴板。</summary>
    void CopyText(string text);
}
