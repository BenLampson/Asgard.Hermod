namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>布局适配器引用公共字段状态，不另建保存、校验或测试实现。</summary>
public sealed class SshConnectionEditorViewModel : InstanceConfigurationEditorViewModel
{
    private string _pane = "target";
    private string? _panesRoute = "";

    /// <summary>连接方式切换时重新读取公共字段，不保留旧路径的凭据引用。</summary>
    public SshConnectionEditorViewModel(NewInstanceViewModel wizard) : base(wizard)
    {
        Attach();
    }
    /// <summary>展示目标与连接路径的整句摘要。</summary>
    public string RouteSummary => IsJump ? Text.Format("SshJumpRouteSummary", Field("jump.host").Value, Field("host").Value)
        : IsProxy ? Text.Format("SshJumpRouteSummary", Field("proxy.host").Value, Field("host").Value)
        : IsBastion ? Text.Format("SshBastionRouteSummary", Host.Value, BastionSelection.Value) : Text.Get("SshDirect");
    /// <summary>菜单型堡垒机只需一组登录身份和目标编号。</summary>
    public bool IsBastion => Wizard.SelectedProvider?.Id == SshIds.Bastion;
    /// <summary>每个会话都发送相同的已保存编号。</summary>
    public InstanceConfigurationFieldViewModel BastionSelection => Field("bastion.selection");
    /// <summary>编号发送前等待的菜单提示符。</summary>
    public InstanceConfigurationFieldViewModel BastionMenuPrompt => Field("bastion.menuPrompt");
    /// <summary>目标使用特殊提示符时的显式后缀。</summary>
    public InstanceConfigurationFieldViewModel BastionShellPrompt => Field("bastion.shellPrompt");
    /// <summary>所选连接路径为跳板。</summary>
    public bool IsJump => Wizard.SelectedProvider?.Id == SshIds.Jump;
    /// <summary>所选连接路径为 TCP 代理。</summary>
    public bool IsProxy => Wizard.SelectedProvider?.Id == SshIds.Proxy;
    /// <summary>只有多节点路径需要分区切换。</summary>
    public bool HasPanes => IsJump || IsProxy;
    /// <summary>公共分段控件显示协议需要的分区，不插入隐藏的第三个选项。</summary>
    public IReadOnlyList<InstanceFieldChoice> Panes { get; private set; } = [];
    /// <summary>以稳定标识绑定公共分段控件，切换分区不清除输入。</summary>
    public string SelectedPane { get => _pane; set => SelectPane(value); }
    /// <summary>目标节点分区。</summary>
    public bool IsTargetPane { get => _pane == "target"; set { if (value) SelectPane("target"); } }
    /// <summary>跳板节点分区。</summary>
    public bool IsJumpPane { get => _pane == "jump"; set { if (value) SelectPane("jump"); } }
    /// <summary>代理设置分区。</summary>
    public bool IsProxyPane { get => _pane == "proxy"; set { if (value) SelectPane("proxy"); } }
    /// <summary>非代理分区使用共用 SSH 认证布局。</summary>
    public bool IsHostPane => !IsProxyPane;
    /// <summary>当前主机地址字段。</summary>
    public InstanceConfigurationFieldViewModel Host => Field(Prefix + "host");
    /// <summary>当前主机端口字段。</summary>
    public InstanceConfigurationFieldViewModel Port => Field(Prefix + "port");
    /// <summary>当前主机登录用户字段。</summary>
    public InstanceConfigurationFieldViewModel User => Field(Prefix + "user");
    /// <summary>稳定认证选项字段。</summary>
    public InstanceConfigurationFieldViewModel Authentication => Field(Prefix + "auth");
    /// <summary>按所选认证显示的密码字段。</summary>
    public InstanceConfigurationFieldViewModel Password => Field(Prefix + "password");
    /// <summary>按所选认证显示的私钥文件字段。</summary>
    public InstanceConfigurationFieldViewModel KeyFile => Field(Prefix + "key");
    /// <summary>按所选认证显示的口令字段。</summary>
    public InstanceConfigurationFieldViewModel Passphrase => Field(Prefix + "passphrase");
    /// <summary>Agent 认证使用本机身份集合，不读取私钥。</summary>
    public bool UsesAgent => Authentication.Value == SshIds.Agent;
    /// <summary>代理协议。</summary>
    public InstanceConfigurationFieldViewModel ProxyType => Field("proxy.type");
    /// <summary>代理主机。</summary>
    public InstanceConfigurationFieldViewModel ProxyHost => Field("proxy.host");
    /// <summary>代理端口。</summary>
    public InstanceConfigurationFieldViewModel ProxyPort => Field("proxy.port");
    /// <summary>代理用户名。</summary>
    public InstanceConfigurationFieldViewModel ProxyUser => Field("proxy.user");
    /// <summary>代理密码。</summary>
    public InstanceConfigurationFieldViewModel ProxyPassword => Field("proxy.password");
    /// <summary>代理 DNS 选项。</summary>
    public InstanceConfigurationFieldViewModel ProxyDns => Field("proxy.dns");
    /// <summary>共享连接超时选项。</summary>
    public InstanceConfigurationFieldViewModel Timeout => Field("timeout");
    private string Prefix => IsJumpPane ? "jump." : "";

    /// <summary>通过稳定字段标识引用公共状态。</summary>
    private InstanceConfigurationFieldViewModel Field(string key) => GetField(key);
    /// <summary>切换仅改变可见分区，保留所有已填写值。</summary>
    private void SelectPane(string pane)
    {
        if (_pane == pane || !Panes.Any(item => item.Id == pane)) return;
        _pane = pane;
        Refresh();
    }
    /// <summary>字段源更换后更新具名布局，不在模块内维护字段订阅。</summary>
    protected override void OnFieldsChanged() => Refresh();

    /// <summary>普通输入仅更新相关摘要，不重新通知整组字段绑定。</summary>
    protected override void OnFieldValueChanged(InstanceConfigurationFieldViewModel item)
    {
        if (item.Key is "host" or "jump.host" or "proxy.host" or "bastion.selection") OnPropertyChanged(nameof(RouteSummary));
        if (item.Key == Prefix + "auth") OnPropertyChanged(nameof(UsesAgent));
    }
    /// <summary>字段引用由协议标识决定，不在视图里比较翻译标签。</summary>
    private void Refresh()
    {
        // 切换分区时保留选项集合，避免重建 ListBoxItem 后丢失键盘焦点。
        if (_panesRoute != Wizard.SelectedProvider?.Id)
        {
            _panesRoute = Wizard.SelectedProvider?.Id;
            _pane = "target";
            Panes = IsJump ? [new("target", Text.Get("SshHost")), new("jump", Text.Get("SshJumpHost"))]
                : IsProxy ? [new("target", Text.Get("SshHost")), new("proxy", Text.Get("SshProxySettings"))]
                : [new("target", Text.Get("SshHost"))];
            OnPropertyChanged(nameof(Panes));
        }
        foreach (string name in new[] { nameof(RouteSummary), nameof(IsBastion), nameof(BastionSelection), nameof(BastionMenuPrompt), nameof(BastionShellPrompt), nameof(IsJump), nameof(IsProxy), nameof(HasPanes), nameof(SelectedPane), nameof(IsTargetPane),
            nameof(IsJumpPane), nameof(IsProxyPane), nameof(IsHostPane), nameof(Host), nameof(Port), nameof(User), nameof(Authentication),
            nameof(Password), nameof(KeyFile), nameof(Passphrase), nameof(UsesAgent), nameof(ProxyType), nameof(ProxyHost), nameof(ProxyPort),
            nameof(ProxyUser), nameof(ProxyPassword), nameof(ProxyDns), nameof(Timeout) }) OnPropertyChanged(name);
    }
}
