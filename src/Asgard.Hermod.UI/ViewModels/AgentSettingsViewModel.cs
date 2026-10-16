using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>旧单套非敏感偏好的兼容状态；生产设置界面使用命名模型目录和设备加密存储。</summary>
public sealed class AgentSettingsViewModel : ObservableObject, IDisposable
{
    private readonly AgentConfiguration _configuration;
    private readonly IAgentPreferenceStore _store;

    /// <summary>加载当前配置，既有内存密钥不回填到设置控件。</summary>
    public AgentSettingsViewModel(AgentConfiguration configuration, IAgentPreferenceStore store)
    {
        _configuration = configuration; _store = store;
        Endpoint = configuration.Current.Endpoint; Model = configuration.Current.Model;
        SaveCommand = new RelayCommand(_ => Save());
        ClearKeyCommand = new RelayCommand(_ =>
        {
            var current = _configuration.Current;
            _configuration.Update(current.Endpoint, current.Model, "");
            ApiKey = ""; Status = Text.Get("AgentKeyCleared"); OnPropertyChanged(nameof(KeyStatus));
        });
    }

    /// <summary>OpenAI 兼容服务的 HTTPS 地址，本机服务允许回环 HTTP。</summary>
    public string Endpoint { get; set => SetProperty(ref field, value); } = "";
    /// <summary>用户模型名称原样保留，不替换模型。</summary>
    public string Model { get; set => SetProperty(ref field, value); } = "";
    /// <summary>仅当前设置窗口的密钥草稿，保存后清除。</summary>
    public string ApiKey { get; set => SetProperty(ref field, value); } = "";
    /// <summary>安全的保存结果。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>只显示是否已配置，不暴露密钥内容。</summary>
    public string KeyStatus => Text.Get(string.IsNullOrEmpty(_configuration.Current.ApiKey) ? "AgentKeyNotSet" : "AgentKeyInMemory");
    /// <summary>显式保存非敏感配置并应用本次运行密钥。</summary>
    public RelayCommand SaveCommand { get; }
    /// <summary>用户可主动清除本进程的密钥。</summary>
    public RelayCommand ClearKeyCommand { get; }

    private void Save()
    {
        try
        {
            string endpoint = Endpoint.Trim(), model = Model.Trim();
            // 空密钥保留同一服务的已配置密钥；更换服务时绝不自动传递旧密钥。
            var current = _configuration.Current;
            string key = ApiKey.Length > 0 ? ApiKey : endpoint == current.Endpoint ? current.ApiKey : "";
            var validation = new AgentConfiguration(); validation.Update(endpoint, model, key);
            _store.Save(new(endpoint, model));
            _configuration.Update(endpoint, model, key);
            ApiKey = ""; Status = Text.Get("AgentSettingsSaved"); OnPropertyChanged(nameof(KeyStatus));
        }
        catch (Exception error) { Status = Text.Error(error, "AgentSettingsSaveFailed"); }
    }

    /// <inheritdoc />
    public void Dispose() => ApiKey = "";
}
