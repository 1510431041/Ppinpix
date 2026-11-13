using Ppinpix.Core.Geometry;
using Ppinpix.Core.Windowing;
using Xunit;

namespace Ppinpix.Core.Tests;

public sealed class SnapEngineTests
{
    private static WindowInfo MakeWin(int x, int y, int w, int h, SnapLevel level, string title = "W") =>
        new(nint.Zero, nint.Zero, new PxRect(x, y, w, h), title, "Cls", level, 0, 0);

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
}
