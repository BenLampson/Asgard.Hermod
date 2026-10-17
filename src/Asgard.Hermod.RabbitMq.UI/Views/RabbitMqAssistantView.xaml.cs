using Asgard.Hermod.RabbitMq.UI.ViewModels;

namespace Asgard.Hermod.RabbitMq.UI.Views;

/// <summary>明确附加按钮冻结当前选中数据，不扫描其他消息或资源。</summary>
public partial class RabbitMqAssistantView : UserControl
{
    private readonly RabbitMqWorkspaceViewModel _workspace;
    public RabbitMqAssistantView(RabbitMqWorkspaceViewModel workspace) { _workspace = workspace; InitializeComponent(); DataContext = workspace.Assistant; }
    private void OnAttachObject(object sender, RoutedEventArgs args)
    {
        var data = _workspace.IsDetails ? _workspace.DetailChart : _workspace.SelectedRow?.Record;
        if (data is not null) _workspace.Assistant.Attach(JsonSerializer.Serialize(new { vhost = _workspace.SelectedVhost, resource = _workspace.Resource.ToString(), snapshot = data.Data }));
    }
    private void OnAttachMessage(object sender, RoutedEventArgs args)
    {
        if (_workspace.SelectedMessage is not { } message) return;
        _workspace.Assistant.Attach(JsonSerializer.Serialize(new
        {
            snapshot = _workspace.SnapshotText,
            originalBytes = message.Message.OriginalBytes,
            truncated = message.Message.IsTruncated,
            payload = _workspace.Payload,
            properties = message.Message.Properties
        }));
    }
}
