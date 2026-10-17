using Asgard.Hermod.UI.Localization;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>网卡展示保留原始字节和可空速率，缺失数据不能变为零。</summary>
public sealed record SshMonitorNetworkItem(SshNetworkSnapshot Value)
{
    /// <summary>接口名。</summary>
    public string Name => Value.Name;
    /// <summary>链路状态。</summary>
    public string Status => Text.Get(Value.State switch { "UP" => "SshMonitorLinkUp", "DOWN" => "SshMonitorLinkDown", _ => "SshMonitorNoData" });
    /// <summary>IPv4 地址，缺少 ip 工具时明确不可用。</summary>
    public string Addresses => Value.Addresses.Length > 0 ? Value.Addresses : Text.Get("SshMonitorNoData");
    /// <summary>MTU 数值原样显示。</summary>
    public string Mtu => Value.Mtu?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? Text.Get("SshMonitorNoData");
    /// <summary>上行速率。</summary>
    public string Sent => SshMonitorViewModel.FormatRate(Value.SentPerSecond);
    /// <summary>下行速率。</summary>
    public string Received => SshMonitorViewModel.FormatRate(Value.ReceivedPerSecond);
    /// <summary>累计发送字节。</summary>
    public string TotalSent => FileSizeText.Format(Value.SentBytes);
    /// <summary>累计接收字节。</summary>
    public string TotalReceived => FileSizeText.Format(Value.ReceivedBytes);
}
