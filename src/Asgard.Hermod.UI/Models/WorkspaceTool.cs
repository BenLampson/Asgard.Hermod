using System.Windows.Input;

namespace Asgard.Hermod.UI.Models;

/// <summary>工作区提供稳定标识、已本地化名称和动作；公共工具面板不理解协议。</summary>
public sealed record WorkspaceTool(string Id, string Title, string Icon, ICommand Command,
    string Group = "", string Hint = "", bool RestoreFocus = true);
