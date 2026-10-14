namespace Asgard.Hermod.MySql;

/// <summary>树分组、元数据节点和真实对象使用同一稳定枚举。</summary>
public enum MySqlObjectKind
{
    Root, Database, Tables, Views, Procedures, Functions, Events,
    Table, View, Procedure, Function, Event, Columns, Indexes, ForeignKeys, Triggers, Parameters,
    Column, Index, ForeignKey, Trigger, Parameter,
}
