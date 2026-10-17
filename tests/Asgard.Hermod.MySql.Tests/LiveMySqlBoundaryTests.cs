using System.Net.Sockets;
using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.MySql;
using Asgard.Hermod.Ssh;
using Xunit;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>验证真实服务器的权限、复合主键和逻辑主机 TLS 转发边界。</summary>
public sealed class LiveMySqlBoundaryTests
{
    public static bool Enabled => LiveMySqlTests.Enabled;
    public static bool TlsEnabled => Enabled && File.Exists(Environment.GetEnvironmentVariable("HERMOD_MYSQL_TEST_CA"));

    [Fact(Skip = "Set HERMOD_MYSQL_TEST_PORT for local integration checks.", SkipUnless = nameof(Enabled))]
    public async Task Server_RespectsColumnPermissionsCompositeKeysAndReadOnlyViews()
    {
        string id = Guid.NewGuid().ToString("N")[..20], database = "hermod_boundary_" + id, user = "hermod_" + id;
        var options = Options(); var factory = new MySqlSessionFactory(new Forwarder());
        await using var admin = await factory.OpenAsync(options, TestContext.Current.CancellationToken);
        try
        {
            var setup = await admin.ExecuteAsync($"CREATE DATABASE `{database}`; CREATE TABLE `{database}`.items(tenant INT, id INT, name VARCHAR(50), PRIMARY KEY(tenant,id)) ENGINE=InnoDB; INSERT INTO `{database}`.items VALUES(1,1,'one'); CREATE VIEW `{database}`.visible_items AS SELECT * FROM `{database}`.items; CREATE USER '{user}'@'%' IDENTIFIED BY ''; GRANT SELECT, UPDATE(name) ON `{database}`.items TO '{user}'@'%'", "", TestContext.Current.CancellationToken);
            Assert.Null(setup.Failure);
            var partial = await admin.ExecuteAsync("SELECT id,name FROM items", database, TestContext.Current.CancellationToken); Assert.Null(partial.Results[0].EditableTable);
            var view = await admin.ExecuteAsync("SELECT tenant,id,name FROM visible_items", database, TestContext.Current.CancellationToken); Assert.Null(view.Results[0].EditableTable);
            var viewPage = await admin.ReadTableAsync(database, "visible_items", 0, TestContext.Current.CancellationToken); Assert.Null(viewPage.EditableTable);
            var parameters = options.Parameters.ToDictionary(); parameters["user"] = user;
            await using var restricted = await factory.OpenAsync(new() { TypeId = MySqlIds.Type, ProviderId = MySqlIds.Direct, Parameters = parameters }, TestContext.Current.CancellationToken);
            var page = await restricted.ReadTableAsync(database, "items", 0, TestContext.Current.CancellationToken); var table = Assert.IsType<MySqlTable>(page.EditableTable);
            Assert.Equal(2, table.Keys.Count); Assert.True(table.Columns.Single(column => column.Name == "name").CanUpdate); Assert.False(table.Columns.Single(column => column.Name == "id").CanUpdate);
            var original = await restricted.ReadRowAsync(table, new Dictionary<string, object?> { ["tenant"] = 1, ["id"] = 1 }, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<MySqlFailure>(() => restricted.SaveAsync(table, [new(original, new Dictionary<string, object?> { ["id"] = 2 })], TestContext.Current.CancellationToken));
            await restricted.SaveAsync(table, [new(original, new Dictionary<string, object?> { ["name"] = "changed" })], TestContext.Current.CancellationToken);
            Assert.Equal("changed", (await restricted.ReadRowAsync(table, original, TestContext.Current.CancellationToken))["name"]);
        }
        finally { await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{database}`; DROP USER IF EXISTS '{user}'@'%'", "", CancellationToken.None); }
    }

    [Fact(Skip = "Set HERMOD_MYSQL_TEST_PORT and HERMOD_MYSQL_TEST_CA for configured local TLS checks.", SkipUnless = nameof(TlsEnabled))]
    public async Task Tls_VerifiesCustomCaAndLogicalHostnameThroughSshTransportContract()
    {
        var options = Options(); var parameters = options.Parameters.ToDictionary(); parameters["tls"] = "verify-full";
        parameters["trust"] = "custom"; parameters["ca"] = Environment.GetEnvironmentVariable("HERMOD_MYSQL_TEST_CA")!;
        var forwarder = new Forwarder(); var factory = new MySqlSessionFactory(forwarder);
        await using (var direct = await factory.OpenAsync(new() { TypeId = MySqlIds.Type, ProviderId = MySqlIds.Direct, Parameters = parameters }, TestContext.Current.CancellationToken))
            Assert.Null((await direct.ExecuteAsync("SELECT 1", "", TestContext.Current.CancellationToken)).Failure);
        parameters["host"] = "mysql-test.local";
        await using (var tunnel = await factory.OpenAsync(new() { TypeId = MySqlIds.Type, ProviderId = MySqlIds.Ssh, Parameters = parameters }, TestContext.Current.CancellationToken))
            Assert.Null((await tunnel.ExecuteAsync("SELECT 1", "", TestContext.Current.CancellationToken)).Failure);
        Assert.Contains("mysql-test.local", forwarder.Hosts);
        parameters["host"] = "wrong-host.local";
        await Assert.ThrowsAsync<MySqlFailure>(() => factory.OpenAsync(new() { TypeId = MySqlIds.Type, ProviderId = MySqlIds.Ssh, Parameters = parameters }, TestContext.Current.CancellationToken));
    }
    private static InstanceConnectionOptions Options() => new()
    {
        TypeId = MySqlIds.Type,
        ProviderId = MySqlIds.Direct,
        Parameters = new Dictionary<string, string> { ["host"] = "127.0.0.1", ["port"] = Environment.GetEnvironmentVariable("HERMOD_MYSQL_TEST_PORT")!, ["user"] = "root", ["tls"] = "required" }
    };
    private sealed class Forwarder : ISshTcpStreamFactory
    {
        internal List<string> Hosts { get; } = [];
        public async Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken = default)
        {
            Hosts.Add(host); var client = new TcpClient();
            try { await client.ConnectAsync("127.0.0.1", port, cancellationToken); return client.GetStream(); }
            catch { client.Dispose(); throw; }
        }
    }
}
