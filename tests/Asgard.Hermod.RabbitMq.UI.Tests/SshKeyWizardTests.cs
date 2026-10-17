using System.Security.Cryptography;
using Asgard.Hermod.Core;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.RabbitMq.UI.Tests;

/// <summary>验证真实 MQ 向导冻结测试私钥，编辑已同步配置时保持双语遮蔽显示。</summary>
[Collection(HeadlessCollection.Name)]
public sealed class SshKeyWizardTests(HeadlessFixture fixture)
{
    /// <summary>源文件在测试后删除，保存和重新编辑仍使用测试过的导入内容。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task TestedKey_SavesWithoutOriginalFile_AndReopensMasked(string language) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pem");
        using var key = RSA.Create(2048);
        await File.WriteAllTextAsync(path, key.ExportPkcs8PrivateKeyPem(), TestContext.Current.CancellationToken);
        try
        {
            var factory = new CapturingFactory();
            var provider = new RabbitMqInstanceProvider(RabbitMqIds.Ssh, factory);
            var registry = new InstanceProviderRegistry([provider]);
            var saves = new CapturingConfigurations(provider);
            using var wizard = new NewInstanceViewModel(registry, saves);
            wizard.SelectedType = Assert.Single(wizard.Types);
            wizard.SelectedProvider = Assert.Single(wizard.Providers);
            for (int step = 0; step < 3 && !wizard.IsInformationStep; step++) wizard.NextCommand.Execute(null);
            Assert.True(wizard.IsInformationStep);
            wizard.Name = "MQ key test";
            foreach (var pair in new Dictionary<string, string>
            {
                ["host"] = "mq.example.test",
                ["user"] = "operator",
                ["ssh.host"] = "jump.example.test",
                ["ssh.user"] = "ssh-user",
                ["ssh.auth"] = SshIds.PrivateKey,
                ["ssh.key"] = path
            }) wizard.Fields.Single(field => field.Key == pair.Key).Value = pair.Value;
            Assert.True(wizard.IsInformationComplete);
            wizard.NextCommand.Execute(null);
            await wizard.TestConnectionCommand.ExecuteAsync();
            Assert.True(wizard.HasSuccessfulConnectionTest);
            string imported = Assert.IsType<InstanceConnectionOptions>(factory.LastConnection).Parameters["ssh.key"];
            Assert.True(InstanceFileContent.IsImported(imported));
            File.Delete(path);
            wizard.NextCommand.Execute(null);
            Assert.True(wizard.IsConfirmationStep);
            await wizard.SubmitCommand.ExecuteAsync();
            Assert.NotNull(wizard.CreatedInstance);
            var saved = Assert.IsType<InstanceConnectionOptions>(saves.Saved);
            Assert.Equal(imported, saved.Parameters["ssh.key"]);

            using var reopened = new NewInstanceViewModel(registry, saves, wizard.CreatedInstance, saved);
            using var trace = new Asgard.Hermod.UI.Tests.WpfBindingTrace();
            var view = new RabbitMqConnectionEditor(reopened);
            using var editor = Assert.IsType<RabbitMqConnectionEditorViewModel>(view.DataContext);
            editor.Pane = "ssh";
            var field = Assert.Single(editor.Fields, field => field.Key == "ssh.key");
            Assert.True(field.IsImportedFile);
            Assert.Equal(Text.Get("SshKeyImported"), field.FileValue);
            Assert.Equal(Text.Get("EnteredHidden"), field.ConfirmationValue);
            view.Measure(new(700, 900)); view.Arrange(new(0, 0, 700, 900)); view.UpdateLayout();
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.DoesNotContain(trace.Messages, message => message.Contains("Error:", StringComparison.Ordinal));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    });

    /// <summary>仅替换 MQ 网络边界，保留生产连接器和共享向导流程。</summary>
    private sealed class CapturingFactory : IRabbitMqSessionFactory
    {
        internal InstanceConnectionOptions? LastConnection { get; private set; }
        public Task<IRabbitMqSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
        {
            LastConnection = options;
            return Task.FromResult<IRabbitMqSession>(new FakeRabbitMq());
        }
    }

    /// <summary>模拟存储边界再次预处理，防止保存时退回原路径重新读取。</summary>
    private sealed class CapturingConfigurations(RabbitMqInstanceProvider provider) : IInstanceConfigurationService
    {
        internal InstanceConnectionOptions? Saved { get; private set; }
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult(Saved);
        public async Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default)
        {
            Saved = await provider.PrepareConnectionAsync(draft.Connection, cancellationToken);
            return new("mq-key", draft.Name, draft.Name, "brand-rabbitmq", [], TypeId: RabbitMqIds.Type, ProviderId: RabbitMqIds.Ssh);
        }
    }
}
