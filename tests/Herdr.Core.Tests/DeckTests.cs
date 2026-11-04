using Herdr.Images;

namespace Herdr.Tests;

public class DeckTests
{
    [Fact]
    public void CompositeFacePreviewKeepsTheIconAreaAndSessionLabelRow()
    {
        var key = new Deck.Key("working", "Fix session!", Tab: 3, TabNumber: 2, Project: "herdr");

        var svg = Deck.KeyFace(key, 0, 0, 1);

        Assert.Contains("x=\"20\" y=\"12\" width=\"80\" height=\"80\"", svg);
        Assert.Contains("Fix session!", svg);
        Assert.Contains("<text ", svg);
    }

    [Fact]
    public void CompositeFacePreviewIncludesTheTabPositionSuperscript()
    {
        var key = new Deck.Key("working", "Fix login", Tab: 3, TabNumber: 2, Project: "herdr");
        var (rgb, style) = TileFaces.StyleFor("working");

        var png = Convert.FromBase64String(Deck.FaceImage(key));
        var expected = FacePng.Draw(new FaceSpec
        {
            Tab = 3,
            TabNumber = 2,
            Project = "herdr",
            RingRgb = rgb,
            RingStyle = style,
            Band = ProjectColor.For("herdr"),
        });

        Assert.Equal(expected, png);
    }

    [Fact]
    public void CompositeFaceCacheDistinguishesTabPositions()
    {
        var first = new Deck.Key("working", "", Tab: 3, TabNumber: 1, Project: "herdr");
        var second = first with { TabNumber = 2 };

        Assert.NotEqual(Deck.FaceImage(first), Deck.FaceImage(second));
    }

    [Fact]
    public void LivePreviewUsesSessionServiceTextAndTheTabPosition()
    {
        var agent = new Agent
        {
            PaneId = "workspace:pane",
            WorkspaceId = "workspace",
            TabId = "workspace:tab-2",
            Kind = "claude",
            AgentStatus = "working",
            TerminalTitleStripped = "Fix login",
        };
        var snapshot = new Snapshot
        {
            Workspaces = new Dictionary<int, Workspace>
            {
                [3] = new()
                {
                    WorkspaceId = "workspace",
                    ActiveTabId = "workspace:tab-2",
                    Number = 3,
                    Label = "project-name",
                    AgentStatus = "working",
                },
            },
            Agents = [agent],
            Recent = [agent],
            Tabs = new Dictionary<string, int> { ["workspace:tab-2"] = 2 },
        };
        var tile = new SpaceTile(new HerdrClient("/nonexistent/herdr.sock"), 3);
        tile.Update(snapshot);

        var key = Deck.KeyFromTile(tile);

        Assert.Equal("Fix login", key.Label);
        Assert.Equal(3, key.Tab);
        Assert.Equal(2, key.TabNumber);
        Assert.Equal("project-name", key.Project);
    }

    [Fact]
    public void StateLegendKeepsItsRingAndLabelApproximation()
    {
        var key = new Deck.Key("blocked", "blocked");

        var svg = Deck.KeyFace(key, 0, 0, 1, rounded: true);

        Assert.Contains("<rect ", svg);
        Assert.Contains("<image ", svg);
        Assert.Contains("blocked", svg);
    }
}
