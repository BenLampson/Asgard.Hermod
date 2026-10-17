namespace Asgard.Hermod.Abstractions.Models;

/// <summary>字段参与的键类别，只作元数据展示，不替代实际写入权限验证。</summary>
public enum DatabaseColumnKeyKind
{
    /// <summary>服务器未提供或无法识别。</summary>
    Unknown,
    /// <summary>没有报告键或索引。</summary>
    None,
    /// <summary>主键成员。</summary>
    Primary,
    /// <summary>唯一约束成员，不声明复合唯一键中的单列独立唯一。</summary>
    Unique,
    /// <summary>索引成员。</summary>
    Indexed,
}
