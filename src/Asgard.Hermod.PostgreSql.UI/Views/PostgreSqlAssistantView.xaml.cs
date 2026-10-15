using Asgard.Hermod.PostgreSql.UI.ViewModels;

namespace Asgard.Hermod.PostgreSql.UI.Views;

/// <summary>侧边输入使用标准键盘导航，Ctrl+Enter 仅发送助手消息。</summary>
public partial class PostgreSqlAssistantView : UserControl
{
    /// <summary>初始化可复制消息与原有 SQL 编辑器的动作入口。</summary>
    public PostgreSqlAssistantView() => InitializeComponent();

    private async void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control || DataContext is not PostgreSqlAssistantViewModel model) return;
        args.Handled = true;
        if (model.SendCommand.CanExecute(null)) { await model.SendCommand.ExecuteAsync(); ConversationScroll.ScrollToEnd(); }
    }
}
