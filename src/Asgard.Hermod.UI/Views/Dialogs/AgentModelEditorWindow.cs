using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>用户显式打开的六百六十乘八百模型编辑窗体。</summary>
public sealed class AgentModelEditorWindow : HermodDialog
{
    /// <summary>窗口仅拥有本次草稿，成功提交后关闭。</summary>
    public AgentModelEditorWindow(AgentModelEditorViewModel model)
    {
        Width = 660; Height = 800; CaptionHeight = 60; Title = model.Title; DataContext = model;
        Resources.MergedDictionaries.Add(new() { Source = new Uri("/Asgard.Hermod.UI;component/Resources/AgentDesign.xaml", UriKind.Relative) });
        CaptionContent = new SettingsCaption { Title = model.Title, IsEditor = true };
        Content = new AgentModelEditorView(); model.CloseRequested += OnCloseRequested;
        Closed += (_, _) => { model.CloseRequested -= OnCloseRequested; model.Dispose(); };
        void OnCloseRequested(object? sender, bool accepted) { DialogResult = accepted; }
    }
}
