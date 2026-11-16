namespace Asgard.Hermod.ObjectStorage.Clients;

/// <summary>进度回调属于展示通知，订阅者失败不能改变远程或本地操作结果。</summary>
public static class ObjectStorageProgress
{
    /// <summary>报告非负进度，不记录回调异常或传输参数。</summary>
    public static void Report(IProgress<ObjectStorageTransferProgress>? progress, long transferred, long total)
    {
        if (progress is not null)
            Report(progress, new ObjectStorageTransferProgress(Math.Max(0, transferred), Math.Max(0, total)));
    }

    /// <summary>进度回调失败不影响后续批次，也不会被当作业务失败。</summary>
    internal static void Report<T>(IProgress<T>? progress, T value)
    {
        try { progress?.Report(value); }
        catch (Exception) { }
    }
}
