namespace Asgard.Hermod.Identity;

/// <summary>结果页状态独立于翻译文案，成功只表示已验证并保存身份。</summary>
internal enum LoginCallbackStatus
{
    Received,
    Succeeded,
    Failed,
    Invalid,
}
