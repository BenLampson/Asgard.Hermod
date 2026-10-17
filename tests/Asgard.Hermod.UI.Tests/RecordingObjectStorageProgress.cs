namespace Asgard.Hermod.UI.Tests;

/// <summary>同步记录传输回调，并可模拟视图通知异常。</summary>
public sealed class RecordingObjectStorageProgress : IProgress<ObjectStorageTransferProgress>
{
    /// <summary>已收到的传输量和总大小。</summary>
    public List<ObjectStorageTransferProgress> Values { get; } = [];
    /// <summary>视图通知失败不能改变传输提交结果。</summary>
    public bool ThrowOnReport { get; init; }
    /// <inheritdoc />
    public void Report(ObjectStorageTransferProgress value)
    {
        Values.Add(value);
        if (ThrowOnReport) throw new InvalidOperationException("模拟传输视图通知失败。");
    }
}
