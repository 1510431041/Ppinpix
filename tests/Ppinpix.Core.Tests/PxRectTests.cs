using Ppinpix.Core.Geometry;
using Xunit;

namespace Ppinpix.Core.Tests;

public sealed class PxRectTests
{
    [Theory]
    [InlineData(10, 20, 100, 50, 10, 20, 110, 70)]
    [InlineData(-1920, 0, 1920, 1080, -1920, 0, 0, 1080)]
    public void FromLtrb_ShouldComputeEdges(int x, int y, int w, int h, int el, int et, int er, int eb)
    {
        var rect = new PxRect(x, y, w, h);
        Assert.Equal(el, rect.Left);
        Assert.Equal(et, rect.Top);
        Assert.Equal(er, rect.Right);
        Assert.Equal(eb, rect.Bottom);
        Assert.Equal(rect, PxRect.FromLtrb(el, et, er, eb));
    }

    [Fact]
    public void FromCorners_ShouldNormalizeReversedCorners()
    {
        var rect = PxRect.FromCorners(new PxPoint(200, 150), new PxPoint(50, 30));
        Assert.Equal(new PxRect(50, 30, 150, 120), rect);
    }

    [Theory]
    [InlineData(10, 20, 100, 50, 10, 20, true)]
    [InlineData(10, 20, 100, 50, 109, 69, true)]
    [InlineData(10, 20, 100, 50, 110, 70, false)] // 半开区间 [Left, Right)
    [InlineData(10, 20, 100, 50, 5, 40, false)]
    public void ContainsPoint_ShouldUseHalfOpenBounds(int l, int t, int w, int h, int px, int py, bool expected)
    {
        var rect = new PxRect(l, t, w, h);
        Assert.Equal(expected, rect.Contains(new PxPoint(px, py)));
    }

    [Fact]
    public void Intersect_Union_ClampTo_ShouldBeExact()
    {
        var a = new PxRect(0, 0, 100, 100);
        var b = new PxRect(50, 50, 100, 100);
        var disjoint = new PxRect(200, 200, 10, 10);

        Assert.Equal(new PxRect(50, 50, 50, 50), a.Intersect(b));
        Assert.Equal(PxRect.Empty, a.Intersect(disjoint));
        Assert.True(a.IntersectsWith(b));
        Assert.False(a.IntersectsWith(disjoint));
        Assert.Equal(new PxRect(0, 0, 150, 150), a.Union(b));
        Assert.Equal(new PxRect(-10, -10, 120, 120), a.Inflate(10));
        Assert.Equal(new PxRect(25, 35, 100, 100), a.Offset(25, 35));
        Assert.Equal(new PxRect(50, 50, 50, 50), b.ClampTo(a));
    }
}
