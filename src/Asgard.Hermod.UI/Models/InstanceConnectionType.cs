namespace Asgard.Hermod.UI.Models;

/// <summary>新建实例表单中的显示选项；实际协议由连接器实现。</summary>
public enum InstanceConnectionType
{
    /// <summary>远程交互终端。</summary>
    SSH,
    /// <summary>容器服务。</summary>
    Docker,
    /// <summary>集群服务。</summary>
    Kubernetes,
    /// <summary>文件传输服务。</summary>
    FTP,
    /// <summary>本地终端。</summary>
    Local,
}
