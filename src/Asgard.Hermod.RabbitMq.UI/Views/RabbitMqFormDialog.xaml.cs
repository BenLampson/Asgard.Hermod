using Asgard.Hermod.RabbitMq.UI.ViewModels;

namespace Asgard.Hermod.RabbitMq.UI.Views;

/// <summary>共享标题和页脚承载模块表单；写请求在确认窗口中完成。</summary>
public partial class RabbitMqFormDialog : HermodDialog
{
    private readonly RabbitMqForm _form;
    public RabbitMqFormDialog(RabbitMqForm form)
    {
        _form = form; InitializeComponent(); DataContext = form;
        form.Completed += OnCompleted; Closed += (_, _) => form.Completed -= OnCompleted;
        Closing += (_, args) => { if (form.IsBusy) args.Cancel = true; };
    }
    private void OnCompleted(object? sender, EventArgs args) { _form.Completed -= OnCompleted; Dispatcher.BeginInvoke(() => DialogResult = true); }
}
