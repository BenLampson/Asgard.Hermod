using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Services;

/// <summary>窗口关闭仅隐藏；应用退出时统一释放，来源关闭不抹去记录。</summary>
public sealed class AgentWindowService : IAgentWindowService, IDisposable
{
    private readonly Dictionary<object, AgentChatWindow> _windows = new(ReferenceEqualityComparer.Instance);
    /// <inheritdoc />
    public void Open(AgentWindowRequest request)
    {
        if (!_windows.TryGetValue(request.Source, out var window))
        {
            window = new(request); _windows.Add(request.Source, window);
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show(); window.Activate();
    }
    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var window in _windows.Values) window.ClosePermanently();
        _windows.Clear();
    }
}
