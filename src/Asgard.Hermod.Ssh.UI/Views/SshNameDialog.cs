using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>使用公共窗口外观输入单个远端名称，最终路径由协议模块验证。</summary>
internal sealed class SshNameDialog : HermodWindow
{
    private readonly TextBox _input;
    internal string Value => _input.Text;

    internal SshNameDialog(string title, string initial)
    {
        Title = title; Width = 460; Height = 210; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 12) });
        _input = new TextBox { Text = initial, MaxLength = 255, MinHeight = 32 };
        System.Windows.Automation.AutomationProperties.SetName(_input, title); body.Children.Add(_input);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = Text.Get("Cancel"), IsCancel = true, MinWidth = 78 };
        var confirm = new Button { Content = Text.Get("SshActionConfirm"), IsDefault = true, MinWidth = 78, Margin = new Thickness(8, 0, 0, 0) };
        confirm.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(Value)) DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(confirm); body.Children.Add(actions); Content = body;
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }
}
