using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实输入确认和 SQLite 删除事务，不对远程服务执行任何操作。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class InstanceDeletionTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task TypedConfirmation_DeletesOnlyAfterExactPhrase(string language) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        using var storage = new LocalStorageTestEnvironment(); using var services = storage.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var repository = services.GetRequiredService<ILocalConfigurationRepository>();
        // 删除路径只操作配置身份，不应尝试解密这个刻意无效的连接载荷。
        repository.CreateInstance(new("delete-test", "Delete test", "storage", "aliyun-oss", [1, 2, 3], DateTime.UtcNow, ["builtin.environment.production"]));
        var configurations = services.GetRequiredService<IInstanceConfigurationService>();
        int deletes = 0;
        var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new DeleteInstanceDialog("Delete test", async () =>
        {
            await configurations.DeleteAsync("delete-test", new(null), TestContext.Current.CancellationToken); deletes++; deleted.SetResult();
        });
        try
        {
            Assert.True(await ModalWindowTestInteraction.ShowAsync(window, async () =>
            {
                var input = WindowTestVisuals.Descendants<TextBox>(window).Single();
                var button = WindowTestVisuals.Descendants<Button>(window).Single(item => Equals(item.Content, Text.Get("MainDeleteInstance")));
                Assert.Empty(input.Text); Assert.False(button.IsEnabled);
                input.Text = Text.Get("MainDeletePhrase") + " "; Assert.False(button.IsEnabled); Assert.NotNull(repository.GetInstance("delete-test"));
                input.Text = Text.Get("MainDeletePhrase"); Assert.True(button.IsEnabled);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await deleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }));
            Assert.Equal(1, deletes); Assert.Null(repository.GetInstance("delete-test"));
            Assert.Equal(0, Convert.ToInt32(services.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("SELECT COUNT(*) FROM instance_tag WHERE instance_id='delete-test';")));
        }
        finally { if (window.IsVisible) window.Close(); Text.SetApplicationCulture(null); }
    });
}
