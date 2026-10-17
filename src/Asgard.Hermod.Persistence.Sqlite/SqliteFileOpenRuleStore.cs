namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>每个后缀使用独立 app_setting 行，避免规则与语言、账户设置互相覆盖。</summary>
public sealed class SqliteFileOpenRuleStore(IFreeSql database) : IFileOpenRuleStore
{
    private const string Prefix = "file.openwith:";

    /// <inheritdoc />
    public IReadOnlyList<FileOpenRule> Load()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT id, value FROM app_setting WHERE id LIKE 'file.openwith:%' ORDER BY id;";
        using var reader = command.ExecuteReader();
        var rules = new List<FileOpenRule>();
        while (reader.Read()) rules.Add(new(reader.GetString(0)[Prefix.Length..], reader.GetString(1)));
        return rules;
    }

    /// <inheritdoc />
    public void Save(FileOpenRule rule, string? originalExtension = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        string? original = originalExtension is null ? null : FileOpenRule.NormalizeExtension(originalExtension);
        using var lease = database.Ado.MasterPool.Get();
        using var transaction = lease.Value.BeginTransaction();
        using var command = lease.Value.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM app_setting WHERE id = @id;";
        AddParameter(command, "@id", Prefix + rule.Extension);
        bool exists = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        if (exists && original != rule.Extension) throw Text.Exception("FileRuleDuplicate", rule.Extension);
        if (original is not null)
        {
            command.Parameters.Clear();
            AddParameter(command, "@id", Prefix + original);
            if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                throw Text.Exception("FileRuleMissing");
        }
        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES (@id, @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        AddParameter(command, "@id", Prefix + rule.Extension);
        AddParameter(command, "@value", rule.ProgramPath);
        AddParameter(command, "@time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        if (original is not null && original != rule.Extension)
        {
            command.CommandText = "DELETE FROM app_setting WHERE id = @old;";
            command.Parameters.Clear();
            AddParameter(command, "@old", Prefix + original);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <inheritdoc />
    public void Remove(string extension)
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "DELETE FROM app_setting WHERE id = @id;";
        AddParameter(command, "@id", Prefix + FileOpenRule.NormalizeExtension(extension));
        command.ExecuteNonQuery();
    }

    /// <summary>用户输入始终作为参数，不能成为 SQL 或命令文本。</summary>
    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
