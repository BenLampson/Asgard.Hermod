using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>删除配置前必须输入当前语言的确认短语，取消和失败不会提前关闭编辑器。</summary>
public sealed class DeleteInstanceDialog : HermodDialog
{
    private readonly TextBox _input = new();
    private readonly Button _delete;
    private readonly Button _cancel;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    private bool _working;

    /// <summary>只调用配置删除服务，不对远程实例或已打开连接执行操作。</summary>
    public DeleteInstanceDialog(string instanceName, Func<Task> delete)
    {
        Title = Text.Get("MainDeleteInstance"); Width = 540; Height = 340;
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = Text.Format("MainDeleteWarning", instanceName), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = Text.Format("MainDeletePrompt", Text.Get("MainDeletePhrase")), Margin = new Thickness(0, 18, 0, 8), TextWrapping = TextWrapping.Wrap });
        System.Windows.Automation.AutomationProperties.SetName(_input, Text.Get("MainDeleteConfirmation"));
        content.Children.Add(_input); _error.SetResourceReference(ForegroundProperty, "DangerBrush"); content.Children.Add(_error); Content = content;
        _delete = new Button { Content = Text.Get("MainDeleteInstance"), IsEnabled = false };
        _delete.SetResourceReference(StyleProperty, "DangerButton");
        _cancel = new Button { Content = Text.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        _cancel.Click += (_, _) => DialogResult = false;
        _input.TextChanged += (_, _) => _delete.IsEnabled = !_working && _input.Text.Equals(Text.Get("MainDeletePhrase"), StringComparison.Ordinal);
        _delete.Click += async (_, _) =>
        {
            if (!_delete.IsEnabled) return;
            _working = true; _delete.IsEnabled = false; _cancel.IsEnabled = false; _input.IsEnabled = false;
            try { await delete(); _working = false; DialogResult = true; }
            catch (Exception error) { _error.Text = Text.Error(error); }
            finally { _working = false; _cancel.IsEnabled = true; _input.IsEnabled = true; _delete.IsEnabled = _input.Text == Text.Get("MainDeletePhrase"); }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(_cancel); buttons.Children.Add(_delete);
        FooterContent = new DialogActionBar { Content = buttons };
        Loaded += (_, _) => _input.Focus();
    }
    /// <summary>删除事务提交中不把窗口关闭误报为取消。</summary>
    protected override void OnClosing(CancelEventArgs args) { if (_working) args.Cancel = true; base.OnClosing(args); }
}
