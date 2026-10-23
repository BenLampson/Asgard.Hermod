using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证只读检查、并发保护、安全失败及应用关闭后的迟到结果。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ApplicationUpdateTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public Task Check_PreservesKnownUpdateOnFailureAndRecovers(string language) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var service = new UpdateTestService();
            using var model = new ApplicationUpdateViewModel(service);
            Assert.False(model.HasUpdate);
            await model.CheckCommand.ExecuteAsync();
            Assert.True(model.HasUpdate);
            Assert.Equal(Text.Get("UpdatesAvailable"), model.Status);
            Assert.Contains("2.0.0.0", model.PublishedVersion);
            Assert.Equal("Release notes", model.ReleaseNotes);
            Assert.Equal(0, service.OpenCount);
            service.Check = _ => throw new InvalidOperationException("secret-server-response");
            await model.CheckCommand.ExecuteAsync();
            Assert.True(model.HasUpdate);
            Assert.Equal(Text.Get("UpdatesCheckFailed"), model.Status);
            Assert.DoesNotContain("secret", model.Status);
            Assert.Equal("Release notes", model.ReleaseNotes);
            service.Check = _ => Task.FromResult(new ApplicationUpdateResult(false, null));
            await model.CheckCommand.ExecuteAsync();
            Assert.False(model.HasUpdate);
            Assert.Equal(Text.Get("UpdatesCurrent"), model.Status);
            Assert.Equal(Text.Get("UpdatesNotesUnavailable"), model.ReleaseNotes);
            Assert.Equal(0, service.OpenCount);
            model.OpenDownloadCommand.Execute(null);
            Assert.Equal(1, service.OpenCount);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task Check_CoalescesRequestsAndIgnoresResultsAfterDisposal() => fixture.RunOffscreenAsync(async () =>
    {
        var pending = new TaskCompletionSource<ApplicationUpdateResult>();
        CancellationToken observed = default;
        var service = new UpdateTestService { Check = token => { observed = token; return pending.Task; } };
        using var model = new ApplicationUpdateViewModel(service);
        var check = model.CheckAsync();
        Assert.True(model.IsChecking);
        Assert.False(model.CheckCommand.CanExecute(null));
        await model.CheckAsync();
        Assert.Equal(1, service.CheckCount);
        model.Dispose();
        Assert.True(observed.IsCancellationRequested);
        pending.SetResult(new(true, new(new Version(3, 0, 0, 0), "late")));
        await check;
        Assert.False(model.HasUpdate);
        Assert.False(model.CheckCommand.CanExecute(null));
        Assert.False(model.OpenDownloadCommand.CanExecute(null));
        Assert.Equal(0, service.OpenCount);
    });

    [Fact]
    public Task StoreAvailability_DoesNotDependOnPublicReleaseVersionOrNotes() => fixture.RunOffscreenAsync(async () =>
    {
        var service = new UpdateTestService { Check = _ => Task.FromResult(new ApplicationUpdateResult(false, new(new Version(9, 0, 0, 0), "pending store review"))) };
        using var model = new ApplicationUpdateViewModel(service);
        await model.CheckAsync();
        Assert.False(model.HasUpdate);
        Assert.Contains("9.0.0.0", model.PublishedVersion);
        service.Check = _ => Task.FromResult(new ApplicationUpdateResult(true, null));
        await model.CheckAsync();
        Assert.True(model.HasUpdate);
        Assert.Equal(Text.Get("UpdatesNotesUnavailable"), model.ReleaseNotes);
    });
}
