namespace Asgard.Hermod.Agent;

/// <summary>模型明确指定的只读查询和数据库，执行资格仍由当前工作区校验。</summary>
public sealed record SqlAgentQuery(string Database, string Sql);
