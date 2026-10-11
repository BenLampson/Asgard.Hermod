namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>上传窗体处理拖拽和焦点；系统选择与路径映射仍由可替换服务负责。</summary>
public partial class ObjectStorageUploadWindow : ObjectStorageDialogWindow
{
    private readonly ObjectStorageUploadViewModel _model;

    /// <summary>绑定未执行远程操作的上传选择状态。</summary>
    public ObjectStorageUploadWindow(ObjectStorageUploadViewModel model)
    {
        _model = model;
        InitializeComponent();
        BindModel(model);
        Loaded += (_, _) => SelectFilesButton.Focus();
    }

    /// <summary>仅接受文件或目录，不将其他拖拽数据作为上传输入。</summary>
    private void OnDragOver(object sender, DragEventArgs args)
    {
        args.Effects = _model.CanAddPaths && args.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    /// <summary>鼠标拖入与系统选择走同一可取消队列入口。</summary>
    private async void OnDrop(object sender, DragEventArgs args)
    {
        args.Handled = true;
        try
        {
            if (_model.CanAddPaths && args.Data.GetData(DataFormats.FileDrop) is string[] paths)
                await _model.AddPathsAsync(paths);
        }
        catch (Exception)
        {
            _model.ReportSelectionError();
        }
    }
}
