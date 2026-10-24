using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Services;

/// <summary>复用独立的非模态设置窗口，重复入口只导航或激活，关闭后释放订阅。</summary>
public sealed class SettingsEditorService : ISettingsEditor, IDisposable, IAsyncDisposable
{
    private readonly Func<Window?> _getOwner;
    private readonly ITagManagementService _tags;
    private readonly ILanguagePreferenceStore _languages;
    private readonly IFileOpenRuleStore? _fileRules;
    private readonly ITemporaryFileStorage? _temporaryFiles;
    private readonly IAccountService? _accounts;
    private readonly IAccountEditor? _accountEditor;
    private readonly IMainWindowClosePreferenceStore? _closePreferences;
    private readonly Agent.AgentConfiguration? _agentConfiguration;
    private readonly IAgentPreferenceStore? _agentPreferences;
    private readonly Agent.AgentModelCatalog? _modelCatalog;
    private readonly Agent.IAgentClient? _agentClient;
    private readonly ApplicationUpdateViewModel? _updates;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private SettingsWindow? _window;
    private Task? _opening;
    private string _requestedCategory = "general";
    private bool _disposed;

    /// <summary>显示边界可在离屏测试中替换；生产路径恢复窗口并激活其有效子编辑器。</summary>
    internal Action<SettingsWindow> PresentWindow { get; init; } = WindowActivation.Show;

    /// <summary>设置独立于主窗口；子编辑器仍以当前设置窗口为所有者。</summary>
    public SettingsEditorService(Func<Window?> getOwner, ITagManagementService tags, ILanguagePreferenceStore languages,
        IFileOpenRuleStore? fileRules = null, ITemporaryFileStorage? temporaryFiles = null,
        IAccountService? accounts = null, IAccountEditor? accountEditor = null, IMainWindowClosePreferenceStore? closePreferences = null,
        Agent.AgentConfiguration? agentConfiguration = null, IAgentPreferenceStore? agentPreferences = null,
        Agent.AgentModelCatalog? modelCatalog = null, Agent.IAgentClient? agentClient = null,
        ApplicationUpdateViewModel? updates = null)
    {
        ArgumentNullException.ThrowIfNull(getOwner);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(languages);
        _getOwner = getOwner;
        _updates = updates;
        _tags = tags;
        _languages = languages;
        _fileRules = fileRules;
        _temporaryFiles = temporaryFiles;
        _accounts = accounts;
        _accountEditor = accountEditor;
        _closePreferences = closePreferences;
        _agentConfiguration = agentConfiguration; _agentPreferences = agentPreferences;
        _modelCatalog = modelCatalog; _agentClient = agentClient;
    }

    /// <inheritdoc />
    public Task OpenAsync() => OpenCategoryAsync(null);
    /// <inheritdoc />
    public Task OpenTagsAsync() => OpenCategoryAsync("tags");
    /// <inheritdoc />
    public Task OpenAgentAsync() => OpenCategoryAsync("agent");
    /// <inheritdoc />
    public Task OpenUpdatesAsync() => OpenCategoryAsync("updates");
    /// <summary>普通入口保留当前页；定向入口可更新尚在初始化的首个请求，不重复加载。</summary>
    private async Task OpenCategoryAsync(string? category)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        if (_window is { } existing)
        {
            if (category is not null && existing.DataContext is SettingsViewModel model) SelectCategory(model, category);
            PresentWindow(existing);
            return;
        }
        if (_opening is not null)
        {
            if (category is not null) _requestedCategory = category;
            await _opening;
            return;
        }
        _requestedCategory = category ?? "general";
        try
        {
            _opening = CreateWindowAsync();
            await _opening;
        }
        finally { _opening = null; }
    }

    /// <summary>模型只随窗口关闭释放；显示或初始化失败、应用退出时同样清理，迟到结果不能开窗。</summary>
    private async Task CreateWindowAsync()
    {
        SettingsWindow? window = null;
        var dialogs = new WindowsTagDialogService(() => window ?? _getOwner(), _tags);
        var storageDialogs = new WindowsStorageSettingsDialogs(() => window ?? _getOwner());
        var model = new SettingsViewModel(_tags, dialogs, _languages, _fileRules, _temporaryFiles, storageDialogs,
            _accounts, _accountEditor is null ? null : () => _accountEditor.OpenAsync(window), _closePreferences, _agentConfiguration, _agentPreferences, _modelCatalog, _agentClient, _updates);
        try
        {
            await model.InitializeAsync(_lifetime.Token);
            if (_disposed) return;
            SelectCategory(model, _requestedCategory);
            window = new SettingsWindow(model);
            window.Closed += OnWindowClosed;
            _window = window;
            PresentWindow(window);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch
        {
            window?.Close();
            throw;
        }
        finally { if (window is null) model.Dispose(); }
    }

    /// <summary>分类使用稳定标识，是否允许切换仍由模型命令判断。</summary>
    private static void SelectCategory(SettingsViewModel model, string category) =>
        model.SelectCategoryCommand.Execute(model.Categories.FirstOrDefault(item => item.Id == category));

    /// <summary>真正关闭后才允许下一次创建；模型由 SettingsWindow 的关闭处理释放。</summary>
    private void OnWindowClosed(object? sender, EventArgs args)
    {
        if (sender is SettingsWindow window) window.Closed -= OnWindowClosed;
        if (ReferenceEquals(_window, sender)) _window = null;
    }

    /// <summary>应用退出时关闭独立设置窗口，并取消尚未完成的初始化。</summary>
    public void Dispose()
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.Invoke(Dispose); return; }
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _window?.Close();
        _lifetime.Dispose();
    }

    /// <summary>宿主异步释放途中可能离开 UI 线程，窗口及模型订阅仍交由原 Dispatcher 回收。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_dispatcher.CheckAccess()) Dispose();
        else await _dispatcher.InvokeAsync(Dispose);
    }
}
