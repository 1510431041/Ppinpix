using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Ppinpix.Capture.Annotation;
using Ppinpix.Capture.Overlay;
using Ppinpix.Capture.Session;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Display;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Ppinpix.Core.Windowing;
using Ppinpix.Theme;
using Xunit;

namespace Ppinpix.Capture.Tests;

public sealed class CaptureSessionTests
{
    private static readonly Lazy<Dispatcher> WpfStaDispatcher = new(() =>
    {
        Dispatcher? created = null;
        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ThemeLoader.EnsureLoaded(app);
            created = app.Dispatcher;
            ready.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();
        return created!;
    });

    [Fact]
    public void SingleClick_OnHighlightedWindow_ShouldSelectHighlightRect()
    {
        var highlightRect = new PxRect(120, 80, 640, 480);
        using var displays = new DisplayService();
        var frame = CreateBlankFrame(new PxRect(0, 0, 1920, 1080));
        var snap = new SnapEngine([
            new WindowInfo(101, 101, highlightRect, "Target", "TestClass", SnapLevel.Window, 0, 0),
        ]);

        using var session = CaptureSession.CreateForTest(new AppSettings(), displays, frame, snap, _ => { });

        session.OnPointerMoved(new PxPoint(300, 200));
        Assert.Equal(highlightRect, session.State.Highlight);
        Assert.False(session.State.HasSelection);

        session.OnButtonDown(new PxPoint(300, 200), isRight: false, isDoubleClick: false);
        session.OnButtonUp(new PxPoint(301, 201));

        Assert.True(session.State.HasSelection);
        Assert.Equal(highlightRect, session.State.Rect);
        Assert.Equal(DragMode.None, session.State.Mode);
    }

    [Fact]
    public void DragBeyondMinimum_ShouldCreateDraggedRectInsteadOfHighlight()
    {
        var highlightRect = new PxRect(100, 100, 800, 600);
        using var displays = new DisplayService();
        var frame = CreateBlankFrame(new PxRect(0, 0, 1920, 1080));
        var snap = new SnapEngine([
            new WindowInfo(101, 101, highlightRect, "Target", "TestClass", SnapLevel.Window, 0, 0),
        ]);
        var settings = new AppSettings { Capture = { SnapEnabled = false } };

        using var session = CaptureSession.CreateForTest(settings, displays, frame, snap, _ => { });

        session.OnPointerMoved(new PxPoint(200, 200));
        session.OnButtonDown(new PxPoint(200, 200), isRight: false, isDoubleClick: false);
        session.OnPointerMoved(new PxPoint(450, 380));
        session.OnButtonUp(new PxPoint(450, 380));

        Assert.True(session.State.HasSelection);
        Assert.Equal(new PxRect(200, 200, 250, 180), session.State.Rect);
        Assert.Equal(DragMode.None, session.State.Mode);
    }

    [Fact]
    public void OnCaptureLost_MidDrag_ShouldResetCreatingStateCleanly()
    {
        using var displays = new DisplayService();
        var frame = CreateBlankFrame(new PxRect(0, 0, 1920, 1080));
        using var session = CaptureSession.CreateForTest(new AppSettings(), displays, frame, new SnapEngine([]), _ => { });

        session.OnButtonDown(new PxPoint(100, 100), isRight: false, isDoubleClick: false);
        Assert.Equal(DragMode.Creating, session.State.Mode);

        session.OnCaptureLost();

        Assert.Equal(DragMode.None, session.State.Mode);
        Assert.False(session.State.HasSelection);
    }

    [Fact]
    public void OverlayWindow_MouseUpAndCaptureLostRaisedOnWindow_ShouldReachInput()
    {
        WpfStaDispatcher.Value.Invoke(() =>
        {
            var bounds = new PxRect(0, 0, 1920, 1080);
            var monitor = new MonitorInfo(1, @"\\.\TEST1", bounds, bounds, 96, true);
            var state = new SelectionState();
            var frame = CreateBlankFrame(bounds);
            var input = new RecordingOverlayInput();
            using var editor = new AnnotationEditor(new AnnotationSettings());

            var overlay = new OverlayWindow(monitor, state, input, editor, frame);
            try
            {
                var expectedCursor = new PxPoint(240, 180);
                overlay.SetFrame(FrameBitmap.GetMonitorView(frame, monitor), bounds);
                overlay.SetCursorPosition(expectedCursor);

                // 当 Window 通过 CaptureMouse() 捕获鼠标时，WPF 将鼠标事件直接投递到 Window 本体；
                // 此处直接向 OverlayWindow 触发 MouseUpEvent，验证处理器挂载在 Window 上而非仅子元素。
                overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseUpEvent,
                });

                Assert.Equal(1, input.UpCount);
                Assert.Equal(expectedCursor, input.LastUpPoint);

                overlay.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = UIElement.LostMouseCaptureEvent,
                });
                Assert.Equal(1, input.CaptureLostCount);
            }
            finally
            {
                overlay.Close();
            }
        });
    }

    private static ScreenFrame CreateBlankFrame(PxRect bounds)
    {
        var pixels = new byte[bounds.Width * bounds.Height * 4];
        return new ScreenFrame(bounds, bounds.Width * 4, pixels);
    }

    private sealed class RecordingOverlayInput : IOverlayInput
    {
        public int UpCount { get; private set; }
        public PxPoint LastUpPoint { get; private set; }
        public int CaptureLostCount { get; private set; }

        public void OnPointerMoved(PxPoint cursor) { }
        public void OnPointerLeft() { }
        public void OnButtonDown(PxPoint cursor, bool isRight, bool isDoubleClick) { }
        public void OnButtonUp(PxPoint cursor)
        {
            UpCount++;
            LastUpPoint = cursor;
        }
        public void OnWheel(PxPoint cursor, int delta) { }
        public void OnKeyDown(Key key, ModifierKeys modifiers) { }
        public void OnCaptureLost() => CaptureLostCount++;
    }
}
