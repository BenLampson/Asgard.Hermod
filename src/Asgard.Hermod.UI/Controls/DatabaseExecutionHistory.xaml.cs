using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.UI.Controls;

/// <summary>只读取当前 Session 的不可变执行快照；筛选与详情切换均不发起数据库请求。</summary>
public partial class DatabaseExecutionHistory : UserControl
{
    /// <summary>独立列表、SQL 与返回详情共用当前选中记录。</summary>
    public DatabaseExecutionHistory() => InitializeComponent();

    private void OnFilter(object sender, FilterEventArgs args)
    {
        string? kind = KindFilter?.SelectedValue as string;
        args.Accepted = kind is null or "all" || args.Item is IDatabaseExecutionTrace trace && (kind == "plan" ? trace.Kind is "explain" or "analyze" : trace.Kind is not ("explain" or "analyze"));
    }
    private void OnKindChanged(object sender, SelectionChangedEventArgs args)
    {
        if (Resources["Traces"] is System.Windows.Data.CollectionViewSource { View: { } view }) view.Refresh();
    }
    private void OnDetailChanged(object sender, RoutedEventArgs args)
    {
        if (SqlSnapshot is null || ReturnedDetails is null) return;
        bool sql = SqlMode.IsChecked == true;
        SqlSnapshot.Visibility = sql ? Visibility.Visible : Visibility.Collapsed;
        ReturnedDetails.Visibility = sql ? Visibility.Collapsed : Visibility.Visible;
    }
}
