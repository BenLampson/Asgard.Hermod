namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>传输业务状态独立于用户界面语言。</summary>
internal enum ObjectStorageTransferState
{
    /// <summary>任务等待执行。</summary>
    Waiting,
    /// <summary>正在传输数据。</summary>
    Transferring,
    /// <summary>任务已完成。</summary>
    Completed,
    /// <summary>用户选择跳过。</summary>
    Skipped,
    /// <summary>任务已取消。</summary>
    Canceled,
    /// <summary>任务执行失败。</summary>
    Failed,
}
