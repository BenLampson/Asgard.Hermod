using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>按页浏览真实文件夹，选择当前目录不会直接保存授权。</summary>
public partial class McpStorageBrowserWindow : HermodDialog
{
    private readonly McpStorageBrowserViewModel _model;
    /// <summary>弹窗不拥有模型；调用方在应用或放弃选择后释放它。</summary>
    public McpStorageBrowserWindow(McpStorageBrowserViewModel model)
    {
        _model = model;
        InitializeComponent(); DataContext = model;
        Loaded += OnLoaded;
    }
    private async void OnLoaded(object sender, RoutedEventArgs args) { Loaded -= OnLoaded; await _model.InitializeAsync(); }
    private void OnChoose(object sender, RoutedEventArgs args) { if (_model.CanChoose) DialogResult = true; }
    private void OnSelectCurrent(object sender, RoutedEventArgs args) => _model.SelectedFolder = null;
    private async void OnOpenFolder(object sender, MouseButtonEventArgs args) => await _model.OpenFolderCommand.ExecuteAsync();
    private async void OnFolderKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        args.Handled = true;
        await _model.OpenFolderCommand.ExecuteAsync();
    }
}
