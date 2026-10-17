using Asgard.Hermod.Redis.UI.ViewModels;

namespace Asgard.Hermod.Redis.UI.Views;

/// <summary>公共窗口拥有滚动与五步流程，此控件只提供 Redis 信息正文。</summary>
public partial class RedisConnectionEditor : UserControl
{
    /// <summary>字段编辑器和分段选择器全部引用公共组件。</summary>
    public RedisConnectionEditor(NewInstanceViewModel wizard)
    {
        InitializeComponent(); DataContext = new RedisConnectionEditorViewModel(wizard);
        Loaded += (_, _) => ((RedisConnectionEditorViewModel)DataContext).Attach();
        Unloaded += (_, _) => ((RedisConnectionEditorViewModel)DataContext).Dispose();
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs args)
    {
        base.OnGotKeyboardFocus(args); if (ReferenceEquals(args.NewFocus, this)) NameInput.Focus();
    }
}
