using Asgard.Hermod.RabbitMq.UI.ViewModels;

namespace Asgard.Hermod.RabbitMq.UI.Views;

/// <summary>公共向导中的模块正文，卸载时解除字段事件。</summary>
public partial class RabbitMqConnectionEditor : UserControl
{
    /// <summary>绑定同一份公共配置草稿。</summary>
    public RabbitMqConnectionEditor(NewInstanceViewModel wizard)
    {
        InitializeComponent(); DataContext = new RabbitMqConnectionEditorViewModel(wizard);
        Loaded += (_, _) => ((RabbitMqConnectionEditorViewModel)DataContext).Attach();
        Unloaded += (_, _) => ((RabbitMqConnectionEditorViewModel)DataContext).Dispose();
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs args)
    { base.OnGotKeyboardFocus(args); if (ReferenceEquals(args.NewFocus, this)) NameInput.Focus(); }
}
