using System.Windows.Controls.Primitives;
using Asgard.Hermod.UI.Common;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.UI.Tests;

/// <summary>只把关闭的 Popup 子树移到离屏根，验证真实工具行与表格内部视口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceInteractionsHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task EmptyToolGroup_DoesNotReserveSecondLineAndDisabledRowsRemainCentered() => fixture.RunOffscreenAsync(() =>
    {
        var tools = new WorkspaceTools
        {
            Items = [new WorkspaceTool("one", "One tool", "file", new RelayCommand(_ => { })),
            new WorkspaceTool("disabled", "Disabled tool", "file", new RelayCommand(_ => { }, _ => false))]
        };
        var popup = (Popup)tools.FindName("ToolsPopup"); var panel = (Border)popup.Child; popup.Child = null;
        ((ItemsControl)tools.FindName("ToolList")).ItemsSource = tools.FilteredTools;
        var root = Root(panel); Layout(root, 340, 380);
        var rows = Descendants<Button>(panel).Where(button => button.DataContext is WorkspaceTool).ToArray();
        Assert.Equal(2, rows.Length);
        foreach (var row in rows)
        {
            var labels = Descendants<TextBlock>(row).ToArray(); var title = labels.Single(label => label.Text == ((WorkspaceTool)row.DataContext).Title);
            Assert.Contains(labels, label => label.Text == "" && label.Visibility == Visibility.Collapsed);
            double center = title.TranslatePoint(new(0, title.ActualHeight / 2), row).Y;
            Assert.InRange(Math.Abs(center - row.ActualHeight / 2), 0, 1);
        }
        Assert.False(rows[1].IsEnabled); Assert.False(popup.IsOpen);
        return Task.CompletedTask;
    });

    [Fact]
    public Task HorizontalWheel_UsesBoundedGridViewportAndPreservesSelection() => fixture.RunOffscreenAsync(async () =>
    {
        var grid = new DataGrid { Style = (Style)Application.Current.FindResource("WorkspaceResultGrid"), ItemsSource = new[] { "row" } };
        for (int index = 0; index < 10; index++) grid.Columns.Add(new DataGridTextColumn { Header = index.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 150 });
        var root = Root(grid); Layout(root, 360, 180); grid.SelectedItem = grid.Items[0]; object selected = grid.SelectedItem;
        var viewer = (ScrollViewer)grid.Template.FindName("DG_ScrollViewer", grid); Assert.True(viewer.ScrollableWidth > 0);
        double vertical = viewer.VerticalOffset;
        Assert.True(WorkspaceGridScroll.ScrollHorizontally(grid, -120)); await WpfTestFixture.DrainDispatcherAsync(); Layout(root, 360, 180);
        Assert.True(viewer.HorizontalOffset > 0); Assert.Equal(vertical, viewer.VerticalOffset); Assert.Same(selected, grid.SelectedItem);
        WorkspaceGridScroll.ScrollHorizontally(grid, int.MinValue); await WpfTestFixture.DrainDispatcherAsync(); Layout(root, 360, 180);
        Assert.Equal(viewer.ScrollableWidth, viewer.HorizontalOffset);
        WorkspaceGridScroll.ScrollHorizontally(grid, int.MaxValue); await WpfTestFixture.DrainDispatcherAsync(); Layout(root, 360, 180);
        Assert.Equal(0, viewer.HorizontalOffset); Assert.Same(selected, grid.SelectedItem);
    });

    [Fact]
    public Task TreeBringIntoView_UsesRowBeforeBaseHandlerCanExpandAncestors() => fixture.RunOffscreenAsync(async () =>
    {
        var template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TreeViewItem">
             <StackPanel><Border x:Name="Row" Height="32"><ContentPresenter ContentSource="Header" /></Border><ItemsPresenter /></StackPanel>
            </ControlTemplate>
            """);
        var parent = new WorkspaceTreeViewItem { Header = "Parent", Template = template };
        var child = new WorkspaceTreeViewItem { Header = "Child", Template = template }; parent.Items.Add(child);
        var tree = new WorkspaceTreeView(); tree.Items.Add(parent); var root = Root(tree); Layout(root, 300, 160);
        child.ApplyTemplate(); parent.IsExpanded = false;
        var requests = new List<(object Target, bool Handled)>();
        root.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, args) => requests.Add((args.TargetObject, args.Handled))), true);
        child.BringIntoView(); await WpfTestFixture.DrainDispatcherAsync();
        Assert.False(parent.IsExpanded);
        Assert.Contains(requests, request => ReferenceEquals(request.Target, child) && request.Handled);
        Assert.Contains(requests, request => request.Target is Border row && row.Name == "Row");
    });
    private static Border Root(UIElement child) { var root = new Border { Child = child }; root.Resources.MergedDictionaries.Add(Application.Current.Resources); return root; }
    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
        Assert.Null(PresentationSource.FromVisual(root)); Assert.Null(Window.GetWindow(root));
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        { var child = VisualTreeHelper.GetChild(root, index); if (child is T match) yield return match; foreach (var descendant in Descendants<T>(child)) yield return descendant; }
    }
}
