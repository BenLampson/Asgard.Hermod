using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Controls;

/// <summary>只读 SQL 和已返回证据的局部阅读，不提供执行入口或创建窗口。</summary>
public partial class DatabaseAgentEvidenceView : UserControl
{
    /// <summary>仅挂载当前证据对象。</summary>
    public DatabaseAgentEvidenceView() { InitializeComponent(); DataContextChanged += (_, _) => CopyFeedback.Text = ""; }
    private void OnViewResult(object sender, RoutedEventArgs args)
    {
        if (DataContext is DatabaseAgentEvidenceViewModel { HasOutput: true } evidence) evidence.IsResultExpanded = !evidence.IsResultExpanded;
    }
    private void OnCopySql(object sender, RoutedEventArgs args)
    {
        if (DataContext is not DatabaseAgentEvidenceViewModel evidence) return;
        try { Clipboard.SetText(evidence.Sql); CopyFeedback.Text = Text.Get("ChatToolSqlCopied"); }
        catch (ExternalException) { CopyFeedback.Text = Text.Get("DatabaseClipboardFailed"); }
    }
}
