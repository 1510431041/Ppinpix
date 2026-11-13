using Ppinpix.Capture.Session;
using Ppinpix.Core.Geometry;
using Xunit;

namespace Ppinpix.Capture.Tests;

public sealed class SelectionStateTests
{
    [Fact]
    public void HitTestHandle_ShouldDetectAllEightHandles()
    {
        var state = new SelectionState { Rect = new PxRect(100, 100, 200, 100) };

        foreach (var handle in SelectionState.AllHandles)
        {
            var center = SelectionState.GetHandleCenter(state.Rect, handle);
            Assert.Equal(handle, state.HitTestHandle(center, radiusPx: 6));
        }

        Assert.Equal(SelectionHandle.None, state.HitTestHandle(new PxPoint(200, 150), radiusPx: 6));
    }

    [Fact]
    public void Resize_ShouldEnforceMinimumSizeAndNeverFlipNegative()
    {
        var origin = new PxRect(100, 100, 100, 100);
        var resized = SelectionState.Resize(origin, SelectionHandle.TopLeft, new PxPoint(500, 500), minimumSize: 5);

        Assert.True(resized.Width >= 5);
        Assert.True(resized.Height >= 5);
        Assert.Equal(origin.Right, resized.Right);
        Assert.Equal(origin.Bottom, resized.Bottom);
    }

    [Fact]
    public void MoveTo_ShouldClampWithinBoundsWithoutShrinking()
    {
        var origin = new PxRect(50, 50, 100, 80);
        var bounds = new PxRect(0, 0, 500, 400);

        var movedRight = SelectionState.MoveTo(origin, 1000, 1000, bounds);
        Assert.Equal(new PxRect(400, 320, 100, 80), movedRight);

        var movedLeft = SelectionState.MoveTo(origin, -1000, -1000, bounds);
        Assert.Equal(new PxRect(0, 0, 100, 80), movedLeft);
    }
}
