namespace Asgard.Hermod.Abstractions.Models;

/// <summary>服务器实际提供的计划形态；字段表不能推断为执行树。</summary>
public enum DatabasePlanFormat
{
    /// <summary>未识别结构，只能阅读原始输出。</summary>
    Raw,
    /// <summary>传统 EXPLAIN 字段表。</summary>
    Fields,
    /// <summary>服务器明确提供父子关系的执行树。</summary>
    Tree
}
