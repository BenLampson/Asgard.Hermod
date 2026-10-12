namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>对象操作子窗体共用关闭与释放契约，业务确认只在显式命令后发生。</summary>
public abstract class ObjectStorageDialogViewModel : ObservableObject, IDisposable
{
    /// <summary>所有窗体的取消和 Escape 都走相同的关闭路径。</summary>
    protected ObjectStorageDialogViewModel() => CancelCommand = new RelayCommand(_ => Close(false), _ => !IsDisposed);

    /// <summary>保存窗体结果后通知模态宿主结束。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>关闭后的异步回调和命令不得继续修改窗体。</summary>
    protected bool IsDisposed { get; private set; }
    /// <summary>不执行业务操作的取消命令。</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>外部关闭同样让模型失效，派生模型负责取消自己的异步工作。</summary>
    public virtual void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        CancelCommand.NotifyCanExecuteChanged();
    }

    /// <summary>同一个窗体只返回一次结果，先终止后台工作再关闭。</summary>
    protected void Close(bool accepted)
    {
        if (IsDisposed)
            return;
        Dispose();
        CloseRequested?.Invoke(this, accepted);
    }
}
