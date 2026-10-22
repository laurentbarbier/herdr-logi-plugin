namespace Herdr.Tests;

public class RecencyTrackerTests
{
    private static Agent A(string paneId, string status = "idle") =>
        new() { PaneId = paneId, WorkspaceId = paneId.Split(':')[0], Kind = "claude", AgentStatus = status, TerminalTitleStripped = paneId };

    private static IEnumerable<string> Ids(IEnumerable<Agent> agents) => agents.Select(a => a.PaneId);

    [Fact]
    public void FirstSnapshotKeepsWorkspaceOrder()
    {
        var tracker = new RecencyTracker();
        Assert.Equal(["w1:p1", "w2:p1", "w3:p1"], Ids(tracker.Rank([A("w1:p1"), A("w2:p1"), A("w3:p1")])));
    }

    [Fact]
    public void AStatusChangeMovesThePaneToTheFront()
    {
        var tracker = new RecencyTracker();
        tracker.Rank([A("w1:p1"), A("w2:p1"), A("w3:p1")]);
        Assert.Equal(["w3:p1", "w1:p1", "w2:p1"], Ids(tracker.Rank([A("w1:p1"), A("w2:p1"), A("w3:p1", "working")])));
    }

    [Fact]
    public void APaneThatAppearsLaterCountsAsActivity()
    {
        var tracker = new RecencyTracker();
        tracker.Rank([A("w1:p1"), A("w2:p1")]);
        Assert.Equal(["w1:p2", "w1:p1", "w2:p1"], Ids(tracker.Rank([A("w1:p1"), A("w2:p1"), A("w1:p2")])));
    }

    [Fact]
    public void RepeatedSnapshotsWithoutStatusChangesKeepTheOrder()
    {
        var tracker = new RecencyTracker();
        tracker.Rank([A("w1:p1"), A("w2:p1"), A("w3:p1")]);
        Assert.Equal(["w1:p1", "w2:p1", "w3:p1"], Ids(tracker.Rank([A("w1:p1"), A("w2:p1"), A("w3:p1")])));
    }

    [Fact]
    public void APaneThatDisappearsAndComesBackIsTreatedAsNewActivity()
    {
        var tracker = new RecencyTracker();
        tracker.Rank([A("w1:p1"), A("w2:p1")]);
        tracker.Rank([A("w2:p1")]);
        Assert.Equal(["w1:p1", "w2:p1"], Ids(tracker.Rank([A("w1:p1"), A("w2:p1")])));
    }

    // The poll loop and a Space key press both rank snapshots, on different threads.
    [Fact]
    public void RankingFromSeveralThreadsAtOnceIsSafe()
    {
        var tracker = new RecencyTracker();
        string[] statuses = ["idle", "working", "blocked", "done"];
        Parallel.For(0, 2000, i =>
        {
            // Panes come and go and change status, so every call adds, updates and removes.
            var agents = Enumerable.Range(0, 1 + i % 20).Select(n => A($"w{n}:p{i % 3}", statuses[(i + n) % 4])).ToList();
            var ranked = tracker.Rank(agents);
            Assert.Equal(agents.Count, ranked.Count);
        });
    }
}
