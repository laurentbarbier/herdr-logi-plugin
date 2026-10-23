using System.Text;
using System.Text.Json;

namespace Herdr.Tests;

public class HerdrClientTests
{
    [Fact]
    public void PaneIdsCompareByTheirNumericParts()
    {
        var sorted = new List<string> { "w1:p10", "w1:p2", "w1:p1", "w10:p1", "w2:p1" };
        sorted.Sort(HerdrClient.ComparePaneIds);
        Assert.Equal(["w1:p1", "w1:p2", "w1:p10", "w2:p1", "w10:p1"], sorted);
    }

    private static Snapshot SnapshotWith(string? focusedWorkspaceId, string focusedPane, string[] panes) => new()
    {
        FocusedWorkspaceId = focusedWorkspaceId,
        Workspaces = new Dictionary<int, Workspace>
        {
            [1] = new() { WorkspaceId = "w1", ActiveTabId = "w1:t1", Number = 1, Label = "one", AgentStatus = "idle" },
        },
        Layouts = new Dictionary<string, Layout>
        {
            ["w1:t1"] = new() { TabId = "w1:t1", FocusedPaneId = focusedPane, Panes = panes.Select(id => new Pane { PaneId = id }).ToList() },
        },
    };

    [Fact]
    public void CyclesToTheNextPaneOnlyWhenTheWorkspaceIsAlreadyFocused()
    {
        Assert.Equal("w1:p2", HerdrClient.NextPaneInFocusedWorkspace(SnapshotWith("w1", "w1:p1", ["w1:p1", "w1:p2"]), "w1"));
        Assert.Equal("w1:p1", HerdrClient.NextPaneInFocusedWorkspace(SnapshotWith("w1", "w1:p2", ["w1:p1", "w1:p2"]), "w1")); // wraps
        Assert.Null(HerdrClient.NextPaneInFocusedWorkspace(SnapshotWith("w2", "w1:p1", ["w1:p1", "w1:p2"]), "w1")); // not focused
        Assert.Null(HerdrClient.NextPaneInFocusedWorkspace(SnapshotWith("w1", "w1:p1", ["w1:p1"]), "w1")); // single pane
    }

    [Fact]
    public async Task ARequestGetsItsResultBack()
    {
        using var herdr = new FakeHerdr();
        herdr.Behaviour = (socket, line) =>
        {
            using var request = JsonDocument.Parse(line);
            var root = request.RootElement;
            var reply = JsonSerializer.Serialize(new
            {
                id = root.GetProperty("id").GetString(),
                result = new { echo = new { method = root.GetProperty("method").GetString(), @params = root.GetProperty("params") } },
            });
            return FakeHerdr.SendAndClose(socket, reply + "\n");
        };
        var result = await new HerdrClient(herdr.SocketPath).RequestAsync("pane.focus", new { pane_id = "w1:p1" });
        Assert.Equal("""{"echo":{"method":"pane.focus","params":{"pane_id":"w1:p1"}}}""", result.GetRawText());
    }

    [Fact]
    public async Task AnErrorReplyThrows()
    {
        using var herdr = new FakeHerdr();
        herdr.Behaviour = (socket, _) => FakeHerdr.SendAndClose(socket, """{"id":"x","error":{"code":"not_found"}}""" + "\n");
        var error = await Assert.ThrowsAsync<HerdrException>(() => new HerdrClient(herdr.SocketPath).RequestAsync("pane.focus"));
        Assert.Contains("not_found", error.Message);
    }

    [Fact]
    public async Task AMultiByteCharacterSplitAcrossChunksArrivesIntact()
    {
        using var herdr = new FakeHerdr();
        herdr.Behaviour = async (socket, _) =>
        {
            var reply = Encoding.UTF8.GetBytes("""{"id":"x","result":{"title":"✳ fix-login"}}""" + "\n");
            var split = reply.AsSpan().IndexOf(Encoding.UTF8.GetBytes("✳")) + 1;
            await socket.SendAsync(reply.AsMemory(0, split));
            await Task.Delay(20);
            await socket.SendAsync(reply.AsMemory(split));
            socket.Shutdown(System.Net.Sockets.SocketShutdown.Both);
        };
        var result = await new HerdrClient(herdr.SocketPath).RequestAsync("session.snapshot");
        Assert.Equal("✳ fix-login", result.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AConnectionClosedWithoutAReplyThrowsInsteadOfHanging()
    {
        using var herdr = new FakeHerdr();
        herdr.Behaviour = (socket, _) => FakeHerdr.SendAndClose(socket, "");
        var error = await Assert.ThrowsAsync<HerdrException>(() => new HerdrClient(herdr.SocketPath).RequestAsync("session.snapshot"));
        Assert.Contains("closed before a response", error.Message);
    }

    [Fact]
    public async Task AMalformedReplyThrowsInsteadOfCrashing()
    {
        using var herdr = new FakeHerdr();
        herdr.Behaviour = (socket, _) => FakeHerdr.SendAndClose(socket, "{oops\n");
        var error = await Assert.ThrowsAsync<HerdrException>(() => new HerdrClient(herdr.SocketPath).RequestAsync("session.snapshot"));
        Assert.Contains("malformed", error.Message);
    }

    [Fact]
    public async Task AMissingSocketThrows()
    {
        using var herdr = new FakeHerdr();
        var client = new HerdrClient(Path.Combine(herdr.Directory, "nope.sock"));
        await Assert.ThrowsAnyAsync<Exception>(() => client.RequestAsync("session.snapshot"));
    }

    [Fact]
    public async Task ASnapshotIsShapedForTheTiles()
    {
        using var herdr = new FakeHerdr();
        herdr.Behaviour = (socket, _) => FakeHerdr.SendAndClose(socket, """
            {"id":"x","result":{"snapshot":{"version":"0.8.2","protocol":20,"focused_workspace_id":"w2",
            "workspaces":[{"workspace_id":"w2","active_tab_id":"w2:t1","number":2,"label":"api","agent_status":"working"},
                          {"workspace_id":"w1","active_tab_id":"w1:t1","number":1,"label":"web","agent_status":"idle"}],
            "agents":[{"pane_id":"w2:p1","workspace_id":"w2","agent":"codex","agent_status":"working","terminal_title_stripped":"api work"},
                      {"pane_id":"w1:p10","workspace_id":"w1","agent":"claude","agent_status":"idle","terminal_title_stripped":"ten"},
                      {"pane_id":"w1:p2","workspace_id":"w1","agent":"claude","agent_status":"blocked","terminal_title_stripped":"two"}],
            "layouts":[{"tab_id":"w2:t1","focused_pane_id":"w2:p1","panes":[{"pane_id":"w2:p1"}]}]}}}
            """.ReplaceLineEndings("") + "\n");
        using var log = new LogCapture();
        var snapshot = await new HerdrClient(herdr.SocketPath).GetSnapshotAsync();
        Assert.Equal("w2", snapshot.FocusedWorkspaceId);
        Assert.Equal("api", snapshot.Workspaces[2].Label);
        Assert.Equal(["w1:p2", "w1:p10", "w2:p1"], snapshot.Agents.Select(a => a.PaneId));
        Assert.Equal("codex", snapshot.Agents[2].Kind);
        Assert.Equal("api work", snapshot.Agents[2].TerminalTitleStripped);
        Assert.Equal("w2:p1", snapshot.Layouts["w2:t1"].FocusedPaneId);
        Assert.Empty(log.Warnings);
    }
}
