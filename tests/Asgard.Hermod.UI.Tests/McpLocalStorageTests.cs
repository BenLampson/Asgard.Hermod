using System.Text.Json;
using Asgard.Hermod.Abstractions.Security;

namespace Asgard.Hermod.UI.Tests;

public sealed class McpLocalStorageTests
{
    [Fact]
    public async Task PolicyIsEncryptedAndGrantFollowsOnlyTheUnchangedConnectionInItsWorkspace()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var environment = new LocalStorageTestEnvironment();
        var configuration = environment.CreateConfiguration();
        var first = new SyncedInstance("exports", "Exports", "storage", "alibaba", new Dictionary<string, string> { ["policy-test-key"] = "synthetic-connection-secret" },
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), []);
        McpGrant grant;
        const string pairing = "synthetic-mcp-pairing-secret";
        using (var services = environment.CreateServices(configuration))
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IMcpLocalStore>();
            var workspace = services.GetRequiredService<ILocalWorkspaceStore>();
            var initial = await workspace.ExportAsync(token);
            Assert.Empty(store.ReadWorkspace().Targets);
            Assert.Null(store.ReadPolicy());
            await workspace.RestoreAsync(initial with { Instances = [first] }, token);
            var target = Assert.Single(store.ReadWorkspace().Targets);
            grant = new("grant", first.Id, first.TypeId, target.Revision, "exports", "nightly/", "", false,
                @"D:\Synthetic transfer folder", AllowUpload: true, AllowDownload: true, AllowCreateFolder: true, AllowDelete: true,
                AllowSignedLinks: true, AllowOverwrite: true, AllowUnrestrictedUpload: true);
            store.SavePolicy(new("", "synthetic-generation", true, pairing, [grant],
                [new("excluded", first.Id, first.TypeId, "exports", "private/")],
                InstanceApprovals: [new(target.Id, target.TypeId, target.Revision)]));

            using var sqlite = new SQLiteConnection(configuration.Database.ConnectionString);
            sqlite.Open();
            using var command = new SQLiteCommand("SELECT value FROM app_setting WHERE id='mcp.localPolicy'", sqlite);
            string ciphertext = Assert.IsType<string>(command.ExecuteScalar());
            Assert.DoesNotContain(pairing, ciphertext);
            var protector = services.GetRequiredService<ISecretProtector>();
            var decrypted = JsonSerializer.Deserialize<McpLocalPolicy>(protector.Unprotect(Convert.FromBase64String(ciphertext)));
            Assert.Equal(pairing, decrypted?.Secret);
        }

        using var reopened = environment.CreateServices(configuration);
        reopened.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var saved = reopened.GetRequiredService<IMcpLocalStore>();
        var local = reopened.GetRequiredService<ILocalWorkspaceStore>();
        var restoredPolicy = saved.ReadPolicy()!;
        Assert.True(restoredPolicy.DefaultAllow);
        Assert.Equal(grant, Assert.Single(restoredPolicy.Grants));
        Assert.Equal("private/", Assert.Single(restoredPolicy.Exclusions!).Qualifier);
        var approval = Assert.Single(restoredPolicy.InstanceApprovals!);
        Assert.True(approval.Matches(Assert.Single(saved.ReadWorkspace().Targets)));
        Assert.Null(saved.OpenTarget("another-owner", grant));
        var initialSnapshot = await local.ExportAsync(token);
        var renamed = initialSnapshot with { Instances = [first with { Name = "Renamed" }] };
        await local.RestoreIfUnchangedAsync(renamed, initialSnapshot, token);
        Assert.True(saved.IsCurrent("", grant));
        Assert.Equal("synthetic-connection-secret", saved.OpenTarget("", grant)?.Parameters["policy-test-key"]);

        var currentSnapshot = await local.ExportAsync(token);
        var retargeted = first with { Parameters = new Dictionary<string, string> { ["policy-test-key"] = "another-connection" } };
        await local.RestoreIfUnchangedAsync(currentSnapshot with { Instances = [retargeted] }, currentSnapshot, token);
        Assert.Null(saved.OpenTarget("", grant));
        Assert.False(saved.IsCurrent("", grant));
        Assert.False(approval.Matches(Assert.Single(saved.ReadWorkspace().Targets)));
        var beforeSwitch = await local.ExportAsync(token);
        await local.SwitchOwnerAsync("another-owner", beforeSwitch, beforeSwitch, token);
        Assert.Equal("another-owner", saved.ReadWorkspace().Owner);
        Assert.Throws<InvalidOperationException>(() => saved.SavePolicy(new("", "stale", true, pairing, [])));
    }
}
