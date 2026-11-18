using Ppinpix.Core.Geometry;
using Ppinpix.Core.Windowing;
using Xunit;

namespace Ppinpix.Core.Tests;

public sealed class SnapEngineTests
{
    private static WindowInfo MakeWin(
        int x, int y, int w, int h, SnapLevel level, string title = "W",
        nint handle = default, nint root = default, int zOrder = 0) =>
        new(handle, root, new PxRect(x, y, w, h), title, "Cls", level, 0, zOrder);

    [Fact]
    public void StackAndProbe_ShouldOrderSmallestToLargest()
    {
        var outer = MakeWin(0, 0, 1000, 800, SnapLevel.Window, "Outer");
        var panel = MakeWin(10, 10, 400, 300, SnapLevel.Child, "Panel");
        var button = MakeWin(20, 20, 80, 30, SnapLevel.Control, "Button");

        var engine = new SnapEngine([outer, button, panel]);
        var pt = new PxPoint(30, 30);

        var stack = engine.Stack(pt);
        Assert.Equal(3, stack.Count);
        Assert.Equal("Button", stack[0].Title);
        Assert.Equal("Panel", stack[1].Title);
        Assert.Equal("Outer", stack[2].Title);

        Assert.Equal("Button", engine.Probe(pt, 0)?.Title);
        Assert.Equal("Panel", engine.Probe(pt, 1)?.Title);
        Assert.Equal("Outer", engine.Probe(pt, 2)?.Title);
        Assert.Equal("Outer", engine.Probe(pt, 99)?.Title);
    }

    [Fact]
    public void Stack_ShouldPreferFrontmostWindowAndKeepItsChildHierarchy()
    {
        var front = MakeWin(100, 100, 400, 300, SnapLevel.Window, "Front", 1, 1, 0);
        var frontChild = MakeWin(120, 120, 80, 50, SnapLevel.Child, "Front child", 2, 1, 0);
        var covered = MakeWin(130, 130, 30, 30, SnapLevel.Window, "Covered", 3, 3, 1);
        var engine = new SnapEngine([covered, frontChild, front]);

        var stack = engine.Stack(new PxPoint(140, 140));

        Assert.Equal(["Front child", "Front"], stack.Select(target => target.Title));
        Assert.Equal("Front child", engine.Probe(new PxPoint(140, 140))?.Title);
        Assert.Equal(2, engine.LevelCount(new PxPoint(140, 140)));
    }

    [Fact]
    public void Probe_ShouldSelectExposedPartOfBackgroundWindow()
    {
        var front = MakeWin(100, 100, 400, 300, SnapLevel.Window, "Front", 1, 1, 0);
        var back = MakeWin(0, 0, 800, 600, SnapLevel.Window, "Back", 2, 2, 1);
        var backChild = MakeWin(10, 10, 80, 50, SnapLevel.Child, "Back child", 3, 2, 0);
        var engine = new SnapEngine([backChild, back, front]);

        Assert.Equal(backChild, engine.Probe(new PxPoint(30, 30)));
        Assert.Equal(back, engine.Probe(new PxPoint(30, 30), 99));
        Assert.Null(engine.Probe(new PxPoint(900, 700)));
    }

    [Fact]
    public void Stack_ShouldIgnoreControlsWithoutARootAtTheCursor()
    {
        var root = MakeWin(100, 100, 400, 300, SnapLevel.Window, "Root", 1, 1, 0);
        var outside = MakeWin(20, 20, 50, 50, SnapLevel.Child, "Outside", 2, 1);
        var orphan = MakeWin(120, 120, 50, 50, SnapLevel.Control, "Orphan", 0, 3);
        var engine = new SnapEngine([root, outside, orphan]);

        Assert.Null(engine.Probe(new PxPoint(30, 30)));
        Assert.Equal(root, engine.Probe(new PxPoint(130, 130)));
    }

    [Fact]
    public void SnapMovingCorner_ShouldSnapWithinThresholdAndRespectDisabled()
    {
        var target = MakeWin(100, 100, 400, 300, SnapLevel.Window, "Target");
        var engine = new SnapEngine([target]);

        var selection = new PxRect(50, 50, 446, 345); // BottomRight = (496, 395), near (500, 400)
        var snapped = engine.SnapMovingCorner(selection, new PxPoint(496, 395), thresholdPx: 8, enabled: true);

        Assert.True(snapped.Snapped);
        Assert.Equal(4, snapped.DeltaX);
        Assert.Equal(5, snapped.DeltaY);
        Assert.Equal(new PxRect(50, 50, 450, 350), snapped.Rect);

        var disabled = engine.SnapMovingCorner(selection, new PxPoint(496, 395), thresholdPx: 8, enabled: false);
        Assert.False(disabled.Snapped);
        Assert.Equal(selection, disabled.Rect);
    }

    [Theory]
    [InlineData(96, 295)]
    [InlineData(345, 96)]
    public void SnapMovingCorner_ShouldNotSnapToAnEdgeFarFromTheMovingCornerAlongItsLength(int x, int y)
    {
        var target = MakeWin(100, 100, 200, 150, SnapLevel.Window, "Target");
        var engine = new SnapEngine([target]);
        var selection = PxRect.FromCorners(new PxPoint(50, 0), new PxPoint(x, y));

        var snapped = engine.SnapMovingCorner(selection, new PxPoint(x, y), thresholdPx: 8);

        Assert.False(snapped.Snapped);
        Assert.Equal(selection, snapped.Rect);
    }

    [Theory]
    [InlineData(150, false)]
    [InlineData(350, true)]
    public void SnapMovingCorner_ShouldOnlySnapToExposedEdges(int y, bool shouldSnap)
    {
        var front = MakeWin(50, 50, 500, 200, SnapLevel.Window, "Front", 1, 1, 0);
        var back = MakeWin(100, 100, 200, 300, SnapLevel.Window, "Back", 2, 2, 1);
        var engine = new SnapEngine([back, front]);
        var selection = PxRect.FromCorners(new PxPoint(0, 0), new PxPoint(296, y));

        var snapped = engine.SnapMovingCorner(selection, new PxPoint(296, y), thresholdPx: 8);

        Assert.Equal(shouldSnap, snapped.Snapped);
        Assert.Equal(shouldSnap ? 4 : 0, snapped.DeltaX);
        Assert.Equal(0, snapped.DeltaY);
        Assert.Equal(shouldSnap ? back : null, snapped.Target);
    }

    [Theory]
    [InlineData(292, 8)]
    [InlineData(308, -8)]
    [InlineData(309, 0)]
    public void SnapMovingCorner_ShouldRespectThresholdOnBothSidesOfAnEdge(int x, int expectedDx)
    {
        var target = MakeWin(100, 100, 200, 300, SnapLevel.Window);
        var selection = PxRect.FromCorners(new PxPoint(0, 0), new PxPoint(x, 200));
        var engine = new SnapEngine([target]);

        var snapped = engine.SnapMovingCorner(selection, new PxPoint(x, 200), thresholdPx: 8);

        Assert.Equal(expectedDx, snapped.DeltaX);
        Assert.Equal(0, snapped.DeltaY);
    }

    [Theory]
    [InlineData(180, false)]
    [InlineData(280, true)]
    public void SnapRect_ShouldOnlyAlignToAnEdgeWithAnExposedSegment(int bottom, bool shouldSnap)
    {
        var front = MakeWin(50, 50, 500, 200, SnapLevel.Window, "Front", 1, 1, 0);
        var back = MakeWin(100, 100, 200, 300, SnapLevel.Window, "Back", 2, 2, 1);
        var engine = new SnapEngine([back, front]);
        var rect = PxRect.FromLtrb(200, 140, 296, bottom);

        var snapped = engine.SnapRect(rect, thresholdPx: 8);

        Assert.Equal(shouldSnap, snapped.Snapped);
        Assert.Equal(shouldSnap ? rect.Offset(4, 0) : rect, snapped.Rect);
    }
}
