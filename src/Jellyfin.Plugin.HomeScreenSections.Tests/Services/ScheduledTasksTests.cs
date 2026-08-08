using Jellyfin.Plugin.HomeScreenSections.Services;
using Jellyfin.Plugin.HomeScreenSections.Tests.Support;
using MediaBrowser.Controller;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

[Collection("Plugin Instance")]
public class ScheduledTasksTests
{
    private readonly PluginFixture m_fixture;

    public ScheduledTasksTests(PluginFixture fixture)
    {
        m_fixture = fixture;
    }

    [Fact]
    public async Task StartupService_registers_index_html_and_matching_chunk_transformations()
    {
        // A web chunk containing the loadSections marker must produce an extra payload.
        string chunkDir = Path.Combine(m_fixture.Paths.WebPath, "main-container");
        Directory.CreateDirectory(chunkDir);
        await File.WriteAllTextAsync(
            Path.Combine(chunkDir, "main.abc123.chunk.js"),
            "var x=1,loadSections:oldFn,otherStuff;");
        // A chunk without the marker must be ignored.
        await File.WriteAllTextAsync(
            Path.Combine(chunkDir, "vendor.def456.chunk.js"),
            "console.log('nothing here');");

        StartupService task = new StartupService(
            new Mock<IServerApplicationHost>().Object,
            m_fixture.Paths,
            NullLogger<HomeScreenSectionsPlugin>.Instance);

        // The FileTransformation plugin is not loaded in tests, so registration is skipped,
        // but the scan + payload-building paths still run.
        await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

        Assert.Equal("HomeScreenSections Startup", task.Name);
        Assert.Equal("Jellyfin.Plugin.HomeScreenSections.Startup", task.Key);
        Assert.Equal("Startup Services", task.Category);
        Assert.False(string.IsNullOrWhiteSpace(task.Description));
    }

    [Fact]
    public void StartupService_triggers_on_startup()
    {
        StartupService task = new StartupService(
            new Mock<IServerApplicationHost>().Object,
            m_fixture.Paths,
            NullLogger<HomeScreenSectionsPlugin>.Instance);

        Assert.Equal(
            TaskTriggerInfoType.StartupTrigger,
            Assert.Single(task.GetDefaultTriggers()).Type);
    }

    [Fact]
    public async Task ImageCacheCleanupTask_reports_progress_and_completes()
    {
        ImageCacheService imageCacheService = new ImageCacheService(
            NullLogger<ImageCacheService>.Instance,
            m_fixture.Paths,
            new HttpClient(FakeHttpMessageHandler.RespondingWithStatus(System.Net.HttpStatusCode.NotFound)));

        ImageCacheCleanupTask task = new ImageCacheCleanupTask(
            imageCacheService,
            NullLogger<ImageCacheCleanupTask>.Instance);

        List<double> progressValues = [];
        Progress<double> progress = new Progress<double>(value => progressValues.Add(value));

        await task.ExecuteAsync(progress, CancellationToken.None);

        Assert.Equal("Home Sections Image Cache Cleanup", task.Name);
        Assert.Equal("Jellyfin.Plugin.HomeScreenSections.ImageCacheCleanup", task.Key);
        Assert.Equal("Maintenance", task.Category);
    }

    [Fact]
    public void ImageCacheCleanupTask_runs_daily_at_three_am()
    {
        ImageCacheService imageCacheService = new ImageCacheService(
            NullLogger<ImageCacheService>.Instance,
            m_fixture.Paths,
            new HttpClient(FakeHttpMessageHandler.RespondingWithStatus(System.Net.HttpStatusCode.NotFound)));

        ImageCacheCleanupTask task = new ImageCacheCleanupTask(
            imageCacheService,
            NullLogger<ImageCacheCleanupTask>.Instance);

        TaskTriggerInfo trigger = Assert.Single(task.GetDefaultTriggers());
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(3).Ticks, trigger.TimeOfDayTicks);
    }

    [Fact]
    public void DailyTranslationCacheService_metadata_and_triggers()
    {
        DailyTranslationCacheService task = new DailyTranslationCacheService(
            m_fixture.TranslationManagerMock.Object);

        Assert.Equal("HSS Daily Translation Cache", task.Name);
        Assert.Equal("Jellyfin.Plugin.HomeScreenSections.DailyTranslationCache", task.Key);
        Assert.Equal("Maintenance", task.Category);

        // Startup trigger plus a daily trigger; ExecuteAsync itself talks to GitHub and is
        // intentionally not exercised in unit tests.
        List<TaskTriggerInfo> triggers = [.. task.GetDefaultTriggers()];
        Assert.Equal(2, triggers.Count);
        Assert.Contains(triggers, t => t.Type == TaskTriggerInfoType.StartupTrigger);
        Assert.Contains(triggers, t => t.Type == TaskTriggerInfoType.DailyTrigger);
    }
}
