using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>呈现对象分类及其服务器元数据，打开对象仍由所属工作区处理。</summary>
public partial class MySqlCategoryView : UserControl
{
    private readonly Func<MySqlTreeNode, Task> _open;
    /// <summary>绑定分类节点及对象打开操作。</summary>
    public MySqlCategoryView(MySqlTabViewModel session, Func<MySqlTreeNode, Task> open, System.Windows.Input.ICommand refresh)
    {
        InitializeComponent(); DataContext = session; _open = open; RefreshButton.Command = refresh;
        PropertyChangedEventManager.AddHandler(session, OnCategoryChanged, nameof(MySqlTabViewModel.OverviewCategory));
        UpdateColumns(session);
    }
    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is MySqlTabViewModel session) UpdateColumns(session);
    }
    private void UpdateColumns(MySqlTabViewModel session)
    {
        bool tables = session.OverviewCategory?.Object.Kind == MySqlObjectKind.Tables;
        foreach (var column in new[] { AutoIncrementColumn, UpdatedColumn, LengthColumn, EngineColumn, RowsColumn, CommentColumn })
            column.Visibility = tables ? Visibility.Visible : Visibility.Collapsed;
        DetailColumn.Visibility = tables ? Visibility.Collapsed : Visibility.Visible;
    }
    private async void OnOpen(object sender, MouseButtonEventArgs args)
    {
        if (sender is DataGridRow { Item: MySqlTreeNode { IsPlaceholder: false } node }) await _open(node);
    }
    private async void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && ItemsGrid.SelectedItem is MySqlTreeNode { IsPlaceholder: false } node)
        { args.Handled = true; await _open(node); }
    }
}
