namespace Asgard.Hermod.Abstractions.Services;

/// <summary>本机主窗体关闭偏好，不参与云端工作区同步。</summary>
public interface IMainWindowClosePreferenceStore
{
    /// <summary>提交后通知当前主窗体即时更新关闭语义。</summary>
    event EventHandler? Changed;
    /// <summary>读取本机偏好；首次或未知设置使用询问与托盘优先默认。</summary>
    MainWindowClosePreference Load();
    /// <summary>原子保存询问开关和动作，并在成功后通知。</summary>
    void Save(MainWindowClosePreference preference);
}
