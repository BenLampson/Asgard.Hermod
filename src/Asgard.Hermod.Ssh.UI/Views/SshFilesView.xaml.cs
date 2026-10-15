using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>文件页复用模型命令；目录双击和 Enter 路径均有显式操作入口。</summary>
public partial class SshFilesView : UserControl
{
    /// <summary>创建原生键盘可达的文件页。</summary>
    public SshFilesView() => InitializeComponent();
    private async void OnOpenEntry(object sender, MouseButtonEventArgs args)
    {
        if (DataContext is SshFilesViewModel { Selected: { } entry } model) await model.OpenAsync(entry);
    }
    private void OnPathKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && DataContext is SshFilesViewModel model) { model.RefreshCommand.Execute(null); args.Handled = true; }
    }
    private async void OnEntryKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && DataContext is SshFilesViewModel { Selected: { IsDirectory: true } entry } model)
        { args.Handled = true; await model.OpenAsync(entry); }
    }
}
