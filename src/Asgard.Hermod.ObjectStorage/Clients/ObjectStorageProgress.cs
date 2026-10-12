namespace Asgard.Hermod.ObjectStorage.Clients;

/// <summary>传输回调属于展示通知，订阅者失败不能改变已经完成的远程或本地操作结果。</summary>
public static class ObjectStorageProgress
{
    /// <summary>报告非负进度，不记录回调异常或传输参数。</summary>
    public static void Report(IProgress<ObjectStorageTransferProgress>? progress, long transferred, long total)
    {
        if (progress is null) return;
        try { progress.Report(new(Math.Max(0, transferred), Math.Max(0, total))); }
        catch (Exception) { }
    }
}
