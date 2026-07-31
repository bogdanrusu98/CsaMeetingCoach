using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class MeetingSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CsaMeetingCoach.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndGet_RoundTripsSession()
    {
        var store = new JsonMeetingSessionStore(_directory);
        var now = DateTimeOffset.UtcNow;
        var session = new MeetingSessionState(
            Guid.NewGuid(),
            TestData.CreatePurpose(),
            MeetingSessionStatus.Active,
            now,
            now,
            Revision: 1,
            Checklist: [],
            Transcript: [],
            RecommendedTasks: [],
            Warnings: []);

        await store.SaveAsync(session, CancellationToken.None);
        var loaded = await store.GetAsync(session.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(session.Id, loaded.Id);
        Assert.Equal(session.Purpose.Objective, loaded.Purpose.Objective);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
