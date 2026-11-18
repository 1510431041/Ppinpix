using System.Runtime.InteropServices;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Interop;
using Ppinpix.Core.Windowing;
using Xunit;

namespace Ppinpix.Core.Tests;

public sealed class WindowEnumeratorTests
{
    // 在物理屏幕外创建原生窗口，验证 Windows 可见性标记，同时避免测试窗口打断用户。
    private static readonly PxRect TestBounds = new(-30000, -30000, 1000, 1000);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Collect_ShouldRequireVisibleRootForWindowAndChildren(bool visible, bool expected)
    {
        using var dpi = new PhysicalPixelScope();
        using var root = new TestWindow(visible);
        using var child = new TestWindow(true, root.Handle);

        var targets = new WindowEnumerator().Collect(TestBounds);

        Assert.Equal(expected, targets.Any(target => target.Handle == root.Handle));
        Assert.Equal(expected, targets.Any(target => target.Handle == child.Handle));
    }

    [Fact]
    public void Collect_ShouldNotReintroduceExcludedOrHiddenRootsThroughUia()
    {
        using var dpi = new PhysicalPixelScope();
        using var hidden = new TestWindow(false);
        using var excluded = new TestWindow(true);
        using var excludedChild = new TestWindow(true, excluded.Handle);
        var uiaBounds = new PxRect(-29950, -29950, 30, 30);

        var targets = new WindowEnumerator().Collect(
            TestBounds, excludeHandles: [excluded.Handle], includeUiaControls: true,
            uiaCollector: (_, _) => [Control(hidden.Handle, uiaBounds), Control(excluded.Handle, uiaBounds)]);

        Assert.DoesNotContain(targets, target => target.RootHandle == hidden.Handle || target.RootHandle == excluded.Handle);
    }

    [Fact]
    public void Collect_ShouldClipUiaBoundsAndKeepControlsWithoutNativeHandles()
    {
        using var dpi = new PhysicalPixelScope();
        using var root = new TestWindow(true);
        var enumerator = new WindowEnumerator();
        var window = Assert.Single(enumerator.Collect(TestBounds, includeChildren: false), target => target.Handle == root.Handle);
        var oversized = window.Bounds.Inflate(50);

        var targets = enumerator.Collect(
            TestBounds, excludeHandles: [nint.Zero], includeUiaControls: true,
            uiaCollector: (_, _) => [Control(root.Handle, oversized)]);

        var control = Assert.Single(targets, target => target.RootHandle == root.Handle && target.Level == SnapLevel.Control);
        Assert.Equal(window.Bounds, control.Bounds);
        Assert.Equal(nint.Zero, control.Handle);
    }

    private static WindowInfo Control(nint root, PxRect bounds) =>
        new(0, root, bounds, "Test control", "TestControl", SnapLevel.Control, 1, 0);

    // 测试宿主没有应用的 DPI manifest，临时采用相同坐标系，避免 150% 缩放导致 DWM 与 user32 坐标不同。
    private sealed class PhysicalPixelScope : IDisposable
    {
        private readonly nint _previous = SetThreadDpiAwarenessContext(DisplayApi.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        public void Dispose() => SetThreadDpiAwarenessContext(_previous);

        [DllImport("user32.dll")]
        private static extern nint SetThreadDpiAwarenessContext(nint context);
    }

    private sealed class TestWindow : IDisposable
    {
        public nint Handle { get; }

        public TestWindow(bool visible, nint parent = default)
        {
            int style = visible ? WindowApi.WS_VISIBLE : 0;
            if (parent != 0) style |= WindowApi.WS_CHILD;
            Handle = WindowApi.CreateWindowEx(
                WindowApi.WS_EX_NOACTIVATE, "STATIC", "Ppinpix enumerator test", style,
                parent == 0 ? TestBounds.X + 100 : 10,
                parent == 0 ? TestBounds.Y + 100 : 10,
                parent == 0 ? 400 : 100, parent == 0 ? 300 : 100,
                parent, 0, 0, 0);
            Assert.NotEqual(nint.Zero, Handle);
            if (parent == 0)
            {
                Assert.Equal(visible, WindowApi.IsWindowVisible(Handle));
            }
        }

        public void Dispose() => WindowApi.DestroyWindow(Handle);
    }
}
