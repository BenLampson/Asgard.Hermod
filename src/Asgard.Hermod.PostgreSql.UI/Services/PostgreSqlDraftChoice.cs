namespace Asgard.Hermod.PostgreSql.UI.Services;

/// <summary>关闭和刷新确认的稳定选择，不依赖翻译。</summary>
public enum PostgreSqlDraftChoice
{
    /// <summary>保留当前页面及草稿。</summary>
    Cancel,
    /// <summary>明确放弃本地草稿，不撤销已提交写入。</summary>
    Discard,
    /// <summary>显式保存，失败时仍保留页面。</summary>
    Save,
}
