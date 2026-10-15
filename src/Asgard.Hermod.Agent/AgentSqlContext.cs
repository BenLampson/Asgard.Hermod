namespace Asgard.Hermod.Agent;

/// <summary>调用方明确选取的 SQL 上下文；结果默认不提供，禁止传入连接参数或凭据。</summary>
public sealed record AgentSqlContext(string Database, string Schema, string Sql, string Results = "", string Dialect = "MySQL", string ScopeId = "", IReadOnlyList<string>? Databases = null);
