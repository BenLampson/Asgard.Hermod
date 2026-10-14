using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.MySql.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class ApplicationCultureTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", "Cancel")]
    [InlineData("zh-Hans", "取消")]
    [InlineData("fr-FR", "Cancel")]
    public Task ApplicationLanguage_RemainsStableWhenAsyncContextRestoresSystemCulture(string preference, string expected) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            Text.SetApplicationCulture(CultureInfo.GetCultureInfo(preference));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            await Task.Yield(); Assert.Equal(expected, Text.Get("Cancel"));
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => Assert.Equal(expected, Text.Get("Cancel")));
        }
        finally { Text.SetApplicationCulture(null); CultureInfo.CurrentUICulture = previous; }
    });
}
