namespace Herdr.Tests;

public class TileBoardTests
{
    private static readonly HerdrClient NoHerdr = new("/nonexistent/herdr.sock");

    private sealed class FakeTile : Tile
    {
        private readonly Action<FakeTile>? update;

        public FakeTile(int slot, string status = "none", Action<FakeTile>? update = null)
            : base(NoHerdr, "Fake", slot, "", "")
        {
            Status = status;
            this.update = update;
        }

        public override void Update(Snapshot snapshot) => update?.Invoke(this);

        public override Task? Press() => null;
    }

    [Fact]
    public void EveryKnownStatusRendersItsOwnImage()
    {
        var images = TileFaces.KnownStatuses.Select(TileFaces.ImageFor).ToList();
        Assert.Equal(7, images.Select(Convert.ToBase64String).Distinct().Count());
        Assert.All(images, image => Assert.NotEmpty(image));
    }

    [Fact]
    public void AStatusThisPluginDoesNotKnowRendersAsUnknownAndIsReportedOnce()
    {
        using var log = new LogCapture();
        Assert.Equal(TileFaces.ImageFor("unknown"), TileFaces.ImageFor("compacting"));
        Assert.Equal(TileFaces.ImageFor("unknown"), TileFaces.ImageFor("compacting"));
        TileFaces.StyleFor("compacting");
        var warning = Assert.Single(log.Warnings);
        Assert.Contains("'compacting'", warning);
    }

    [Fact]
    public void ATileThatCannotBeUpdatedIsReportedOnceAndDoesNotStopTheOthers()
    {
        using var log = new LogCapture();
        var broken = new FakeTile(1, update: _ => throw new InvalidOperationException("snapshot shape changed"));
        var after = new FakeTile(2, update: t => t.Status = "done");
        var changed = new List<Tile>();
        var board = new TileBoard(NoHerdr, [broken, after], changed.Add);

        board.Apply(Snapshot.Empty);
        board.Apply(Snapshot.Empty);

        var error = Assert.Single(log.Errors);
        Assert.Contains("herdr_fake_1", error);
        // The tile after the broken one still updated, and only announced the change.
        Assert.Equal([after], changed);
        Assert.Equal("done", after.Status);
    }

    [Fact]
    public void NoSnapshotShowsEveryTileAsOffline()
    {
        var tiles = new[] { new FakeTile(1, "working"), new FakeTile(2, "idle") };
        var changed = new List<Tile>();

        new TileBoard(NoHerdr, tiles, changed.Add).Apply(null);

        Assert.All(tiles, t => Assert.Equal(("offline", "offline"), (t.Status, t.Label)));
        Assert.Equal(tiles, changed);
    }

    [Fact]
    public void ATileWhoseFaceIsUnchangedIsNotRedrawn()
    {
        var tile = new FakeTile(1, "idle");
        var changed = new List<Tile>();

        new TileBoard(NoHerdr, [tile], changed.Add).Apply(Snapshot.Empty);

        Assert.Empty(changed);
    }
}
