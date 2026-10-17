namespace Asgard.Hermod.UI.Models;

/// <summary>提示的语义类别，决定图标及主题颜色，不通过翻译后的标题判断行为。</summary>
public enum MessageDialogKind
{
    /// <summary>操作确实失败，沿用现有错误提示。</summary>
    Error,
    /// <summary>部分能力暂时不可用，应用仍能继续使用。</summary>
    Warning,
    /// <summary>正常状态说明，例如已唤回运行中的应用。</summary>
    Information,
}
