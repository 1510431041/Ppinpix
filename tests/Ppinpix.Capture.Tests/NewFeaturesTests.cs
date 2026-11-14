using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ppinpix.Capture.Annotation;
using Ppinpix.Capture.Annotation.Shapes;
using Ppinpix.Capture.Longshot;
using Ppinpix.Capture.Overlay;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Xunit;

namespace Ppinpix.Capture.Tests;

public sealed class NewFeaturesTests : IDisposable
{
    private readonly string _tempDir;

    public NewFeaturesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PpinpixNewFeaturesTests_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void SpotlightShape_OverlappingAndRounded_ShouldKeepUnionUnDimmedAndDimOutside()
    {
        var frame = CreateSolidFrame(120, 100, b: 200, g: 200, r: 200, a: 255);
        using var editor = new AnnotationEditor(new AnnotationSettings());
        editor.SetTool(AnnotationTool.Spotlight);
        editor.UpdateStyle(s => s with { CornerRadius = 12 });

        // 第一个聚光灯 [20, 20] -> [70, 70]
        editor.OnPointerDown(20, 20, handleHitRadiusPx: 8);
        editor.OnPointerMove(70, 70, constrain: false, hitRadiusPx: 8);
        editor.OnPointerUp(70, 70, constrain: false);

        // 第二个重叠聚光灯 [50, 40] -> [100, 85]
        editor.OnPointerDown(50, 40, handleHitRadiusPx: 8);
        editor.OnPointerMove(100, 85, constrain: false, hitRadiusPx: 8);
        editor.OnPointerUp(100, 85, constrain: false);

        Assert.Equal(2, editor.Shapes.Count);
        Assert.All(editor.Shapes, s => Assert.IsType<SpotlightShape>(s));

        var rendered = editor.RenderToFrame(frame, new PxRect(0, 0, 120, 100));

        // 单独位于第一个聚光灯内部保持原亮度
        rendered.GetPixel(30, 30, out byte b1, out byte g1, out byte r1, out _);
        Assert.InRange(r1, 195, 205);
        Assert.Equal((r1, r1), (g1, b1));

        // 两个聚光灯重叠区域（58, 55）不得被叠加压暗
        rendered.GetPixel(58, 55, out byte bOv, out byte gOv, out byte rOv, out _);
        Assert.InRange(rOv, 195, 205);
        Assert.Equal((rOv, rOv), (gOv, bOv));

        // 聚光灯外部像素（5, 5）应被 MaskAlpha 压暗
        rendered.GetPixel(5, 5, out _, out _, out byte rOut, out _);
        Assert.True(rOut < 110, $"Expected outside pixel dimmed below 110, actual={rOut}");

        // 圆角外切角（21, 21）应属于被压暗区域而非聚光灯亮区
        rendered.GetPixel(21, 21, out _, out _, out byte rCorner, out _);
        Assert.True(rCorner < 110, $"Expected rounded corner outside pixel dimmed, actual={rCorner}");
    }

    [Fact]
    public void RoundedShapes_CornerRadiusHandleAndRendering_ShouldWorkForRectMosaicBlur()
    {
        var style = new ShapeStyle
        {
            Argb = 0xFFFF0000u,
            LineWidth = 2,
            CornerRadius = 16
        };

        var rectShape = new RectangleShape(new RectF(10, 10, 80, 60), style);
        var cornerHandle = Assert.Single(rectShape.GetHandles(), h => h.Kind == ShapeHandleKind.CornerRadius);

        // 向右拖拽圆角手柄增大圆角，再渲染确认四角外侧保持背景色
        rectShape.MoveHandle(ShapeHandleKind.CornerRadius, cornerHandle.X + 12, cornerHandle.Y, constrain: false);
        Assert.True(rectShape.Style.CornerRadius > 16);

        var frame = CreateCheckerFrame(120, 100);
        using var editor = new AnnotationEditor(new AnnotationSettings());
        editor.SetTool(AnnotationTool.Mosaic);
        editor.UpdateStyle(_ => style);
        editor.OnPointerDown(10, 10, handleHitRadiusPx: 8);
        editor.OnPointerMove(90, 70, constrain: false, hitRadiusPx: 8);
        editor.OnPointerUp(90, 70, constrain: false);

        editor.SetTool(AnnotationTool.Blur);
        editor.UpdateStyle(_ => style);
        editor.OnPointerDown(10, 10, handleHitRadiusPx: 8);
        editor.OnPointerMove(90, 70, constrain: false, hitRadiusPx: 8);
        editor.OnPointerUp(90, 70, constrain: false);

        var mosaic = Assert.IsType<MosaicShape>(editor.Shapes[0]);
        var blur = Assert.IsType<BlurShape>(editor.Shapes[1]);
        Assert.Contains(mosaic.GetHandles(), h => h.Kind == ShapeHandleKind.CornerRadius);
        Assert.Contains(blur.GetHandles(), h => h.Kind == ShapeHandleKind.CornerRadius);

        var rendered = editor.RenderToFrame(frame, new PxRect(0, 0, 120, 100));

        // 马赛克/模糊圆角外的顶点 (11, 11) 不应被滤镜覆盖，保持原始帧像素
        frame.GetPixel(11, 11, out byte ob, out byte og, out byte or_, out _);
        rendered.GetPixel(11, 11, out byte rb, out byte rg, out byte rr, out _);
        Assert.Equal((ob, og, or_), (rb, rg, rr));
    }

    [Fact]
    public void ArrowShape_CurveHandleDragAndDoubleClickReset_ShouldSupportUndoRedo()
    {
        using var editor = new AnnotationEditor(new AnnotationSettings());
        editor.SetTool(AnnotationTool.Arrow);
        editor.UpdateStyle(s => s with { Argb = 0xFF00FF00u, LineWidth = 4 });

        editor.OnPointerDown(20, 60, handleHitRadiusPx: 8);
        editor.OnPointerMove(100, 60, constrain: false, hitRadiusPx: 8);
        editor.OnPointerUp(100, 60, constrain: false);

        var arrow = Assert.IsType<ArrowShape>(Assert.Single(editor.Shapes));
        Assert.False(arrow.IsCurved);

        // 切换选择工具并拖拽曲线控制手柄弯曲箭头
        editor.SetTool(AnnotationTool.Select);
        editor.OnPointerDown(60, 60, handleHitRadiusPx: 8);
        var curveHandle = Assert.Single(arrow.GetHandles(), h => h.Kind == ShapeHandleKind.CurveControl);

        editor.OnPointerDown(curveHandle.X, curveHandle.Y, handleHitRadiusPx: 10);
        editor.OnPointerMove(60, 20, constrain: false, hitRadiusPx: 10);
        editor.OnPointerUp(60, 20, constrain: false);

        var curvedArrow = Assert.IsType<ArrowShape>(Assert.Single(editor.Shapes));
        Assert.True(curvedArrow.IsCurved);
        Assert.True(curvedArrow.CurvePeak.Y < 45);

        // 渲染到画面验证弧峰有像素而直线中点为空背景
        var blank = CreateSolidFrame(120, 100, b: 10, g: 10, r: 10, a: 255);
        var rendered = editor.RenderToFrame(blank, new PxRect(0, 0, 120, 100));
        int peakY = (int)Math.Round(curvedArrow.CurvePeak.Y);
        bool hitPeakPixel = false;
        for (int dy = -2; dy <= 2; dy++)
        {
            rendered.GetPixel(60, Math.Clamp(peakY + dy, 0, 99), out _, out byte g, out _, out _);
            if (g > 150) hitPeakPixel = true;
        }
        Assert.True(hitPeakPixel, "Expected curved arrow pixels near curve peak.");
        rendered.GetPixel(60, 60, out _, out byte straightMidG, out _, out _);
        Assert.InRange(straightMidG, 8, 15);

        // 双击曲线弧身重置为直线，并支持撤销/重做
        editor.OnPointerDown(curvedArrow.CurvePeak.X, curvedArrow.CurvePeak.Y, handleHitRadiusPx: 10, clickCount: 2);
        Assert.False(Assert.IsType<ArrowShape>(Assert.Single(editor.Shapes)).IsCurved);

        Assert.True(editor.Undo());
        Assert.True(Assert.IsType<ArrowShape>(Assert.Single(editor.Shapes)).IsCurved);

        Assert.True(editor.Redo());
        Assert.False(Assert.IsType<ArrowShape>(Assert.Single(editor.Shapes)).IsCurved);
    }

    [Fact]
    public void NumberBadgeShape_DragShouldPullPointerTailWhileKeepingCenterFixed()
    {
        using var editor = new AnnotationEditor(new AnnotationSettings());
        editor.SetTool(AnnotationTool.NumberBadge);
        editor.UpdateStyle(s => s with { Argb = 0xFFFF4500u, LineWidth = 2, FontSize = 18 });

        // 1. 单击放置：无尾巴
        editor.OnPointerDown(35, 35, handleHitRadiusPx: 8);
        editor.OnPointerUp(35, 35, constrain: false);
        var noTail = Assert.IsType<NumberBadgeShape>(editor.Shapes[0]);
        Assert.False(noTail.HasTail);

        // 2. 拖拽超出半径 + 6px：圆心锚定在起点，拉出指针尖尾
        editor.OnPointerDown(80, 60, handleHitRadiusPx: 8);
        editor.OnPointerMove(135, 95, constrain: false, hitRadiusPx: 8);
        editor.OnPointerUp(135, 95, constrain: false);

        var withTail = Assert.IsType<NumberBadgeShape>(editor.Shapes[1]);
        Assert.True(withTail.HasTail);
        Assert.Equal(80, withTail.CenterX, 1);
        Assert.Equal(60, withTail.CenterY, 1);
        Assert.Equal(135, withTail.TailX, 1);
        Assert.Equal(95, withTail.TailY, 1);

        var tailHandle = Assert.Single(withTail.GetHandles(), h => h.Kind == ShapeHandleKind.TailEnd);
        Assert.Equal(135, tailHandle.X, 1);

        var frame = CreateSolidFrame(160, 120, b: 255, g: 255, r: 255, a: 255);
        var rendered = editor.RenderToFrame(frame, new PxRect(0, 0, 160, 120));
        // 指针尾路径靠中心点一侧应存在非白色像素
        rendered.GetPixel(115, 82, out byte bTail, out _, out _, out _);
        Assert.True(bTail < 200, $"Expected badge pointer tail to paint pixel at (115,82), b={bTail}");
    }

    [Fact]
    public void LongshotStitcher_ShouldStitchScrolledFramesDetectNoMovementAndDeduplicateStickyFooter()
    {
        // 生成足够高纹理的非周期长画布 240 x 360
        byte[] fullCanvas = CreateHighTextureCanvas(240, 360);
        var frame1 = CropCanvas(fullCanvas, 240, yOffset: 0, height: 200);
        var frame2 = CropCanvas(fullCanvas, 240, yOffset: 48, height: 200);
        var frame3 = CropCanvas(fullCanvas, 240, yOffset: 96, height: 200);

        var stitcher = new LongshotStitcher(frame1);
        Assert.Equal(200, stitcher.CurrentHeight);

        var step1 = stitcher.AppendFrame(frame2);
        Assert.Equal(StitchStatus.Stitched, step1.Status);
        Assert.Equal(48, step1.ShiftPx);
        Assert.Equal(248, step1.TotalHeightPx);

        var step2 = stitcher.AppendFrame(frame3);
        Assert.Equal(StitchStatus.Stitched, step2.Status);
        Assert.Equal(48, step2.ShiftPx);
        Assert.Equal(296, step2.TotalHeightPx);
        Assert.Equal(3, stitcher.FrameCount);

        var noMove = stitcher.AppendFrame(frame3);
        Assert.Equal(StitchStatus.NoMovement, noMove.Status);
        Assert.Equal(296, noMove.TotalHeightPx);

        var finalFrame = stitcher.BuildFrame();
        Assert.Equal(296, finalFrame.Height);
        // 验证拼接后最后一行像素与画布 row 295 一致
        int row295Offset = 295 * 240 * 4 + 80 * 4;
        finalFrame.GetPixel(80, 295, out byte fb, out byte fg, out byte fr, out _);
        Assert.Equal((fullCanvas[row295Offset], fullCanvas[row295Offset + 1], fullCanvas[row295Offset + 2]), (fb, fg, fr));

        var preview = stitcher.BuildPreviewFrame(maxWidth: 120, maxHeight: 150);
        Assert.True(preview.Width <= 120 && preview.Height <= 150);

        // 固定底栏去重测试：底部 20 行锁定静态彩色条纹
        byte[] footerStrip = CreateStaticFooterStrip(240, 20);
        var f1WithFooter = OverlayFooter(frame1, footerStrip, 20);
        var f2WithFooter = OverlayFooter(frame2, footerStrip, 20);

        var footerStitcher = new LongshotStitcher(f1WithFooter);
        var footerStep = footerStitcher.AppendFrame(f2WithFooter);
        Assert.Equal(StitchStatus.Stitched, footerStep.Status);
        Assert.Equal(48, footerStep.ShiftPx);
        Assert.Equal(248, footerStep.TotalHeightPx);

        var stitchedFooterFrame = footerStitcher.BuildFrame();
        // row 185 在滚动区域内应来自 canvas row 185，而不是被旧帧的固定底栏重复污染
        int c185 = 185 * 240 * 4 + 100 * 4;
        stitchedFooterFrame.GetPixel(100, 185, out byte sb, out byte sg, out byte sr, out _);
        Assert.Equal((fullCanvas[c185], fullCanvas[c185 + 1], fullCanvas[c185 + 2]), (sb, sg, sr));

        // 固定底栏保留在整张长图末尾 row 247
        int fLast = (20 - 1) * 240 * 4 + 100 * 4;
        stitchedFooterFrame.GetPixel(100, 247, out byte lb, out byte lg, out byte lr, out _);
        Assert.Equal((footerStrip[fLast], footerStrip[fLast + 1], footerStrip[fLast + 2]), (lb, lg, lr));
    }

    [Fact]
    public void ToolbarVisibilitySettings_RoundTripAndCaptureToolbarFiltering_ShouldWork()
    {
        string file = Path.Combine(_tempDir, "toolbar-settings.json");
        var store = new SettingsStore(file);

        var settings = new AppSettings();
        settings.Capture.Toolbar.ShowSpotlight = false;
        settings.Capture.Toolbar.ShowLongshot = false;
        settings.Capture.Toolbar.ShowOcr = false;
        settings.Capture.Toolbar.ShowTranslate = false;
        store.Save(settings);

        var loaded = store.Load();
        Assert.False(loaded.Capture.Toolbar.ShowSpotlight);
        Assert.False(loaded.Capture.Toolbar.ShowLongshot);
        Assert.False(loaded.Capture.Toolbar.ShowOcr);
        Assert.False(loaded.Capture.Toolbar.ShowTranslate);
        Assert.True(loaded.Capture.Toolbar.ShowCopy);

        loaded.Capture.Toolbar.ShowAll();
        Assert.True(loaded.Capture.Toolbar.ShowSpotlight);
        Assert.True(loaded.Capture.Toolbar.ShowLongshot);

        // 在 STA 线程构建真实 WPF CaptureToolbar，确认隐藏配置会减少按钮数量且不残留多余分隔线
        Exception? uiEx = null;
        int fullButtons = 0;
        int filteredButtons = 0;
        bool consecutiveSeparatorFound = false;

        var sta = new Thread(() =>
        {
            CaptureToolbar? fullToolbar = null;
            CaptureToolbar? filteredToolbar = null;
            try
            {
                var actions = new StubToolbarActions();
                fullToolbar = new CaptureToolbar(actions, annotationOnly: false, new ToolbarVisibilitySettings());
                fullButtons = CountLogicalDescendants<System.Windows.Controls.Button>(fullToolbar);

                var hidden = new ToolbarVisibilitySettings
                {
                    ShowSpotlight = false,
                    ShowLongshot = false,
                    ShowOcr = false,
                    ShowTranslate = false,
                    ShowUndoRedo = false,
                };
                filteredToolbar = new CaptureToolbar(actions, annotationOnly: false, hidden);
                filteredButtons = CountLogicalDescendants<System.Windows.Controls.Button>(filteredToolbar);
                consecutiveSeparatorFound = HasAdjacentSeparators(filteredToolbar);
            }
            catch (Exception ex)
            {
                uiEx = ex;
            }
            finally
            {
                fullToolbar?.Close();
                filteredToolbar?.Close();
            }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        Assert.Null(uiEx);
        Assert.True(fullButtons > filteredButtons, $"full={fullButtons}, filtered={filteredButtons}");
        Assert.True(fullButtons - filteredButtons >= 6, $"Expected at least 6 hidden buttons, full={fullButtons}, filtered={filteredButtons}");
        Assert.False(consecutiveSeparatorFound);
    }

    private static int CountLogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = 0;
        foreach (object? child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T) count++;
            if (child is DependencyObject dep) count += CountLogicalDescendants<T>(dep);
        }
        return count;
    }

    private static bool HasAdjacentSeparators(DependencyObject root)
    {
        if (root is Panel panel)
        {
            bool prevWasSep = false;
            foreach (UIElement child in panel.Children)
            {
                bool isSep = child is Border b && b.Width <= 2 && b.Child is null;
                if (isSep && prevWasSep) return true;
                prevWasSep = isSep;
            }
        }
        foreach (object? child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dep && HasAdjacentSeparators(dep)) return true;
        }
        return false;
    }

    private static ScreenFrame CreateSolidFrame(int w, int h, byte b, byte g, byte r, byte a)
    {
        int stride = w * 4;
        byte[] buf = new byte[stride * h];
        for (int i = 0; i < buf.Length; i += 4)
        {
            buf[i] = b;
            buf[i + 1] = g;
            buf[i + 2] = r;
            buf[i + 3] = a;
        }
        return new ScreenFrame(new PxRect(0, 0, w, h), stride, buf);
    }

    private static ScreenFrame CreateCheckerFrame(int w, int h)
    {
        int stride = w * 4;
        byte[] buf = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * stride + x * 4;
                bool dark = ((x / 6) + (y / 6)) % 2 == 0;
                buf[idx] = (byte)(dark ? 40 : 210);
                buf[idx + 1] = (byte)(dark ? 80 : 180);
                buf[idx + 2] = (byte)(dark ? 160 : 60);
                buf[idx + 3] = 255;
            }
        }
        return new ScreenFrame(new PxRect(0, 0, w, h), stride, buf);
    }

    private static byte[] CreateHighTextureCanvas(int w, int h)
    {
        int stride = w * 4;
        byte[] buf = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                uint hash = (uint)(x * 73856093 ^ y * 19349663 ^ (x + y) * 83492791);
                int idx = y * stride + x * 4;
                buf[idx] = (byte)(hash & 0xFF);
                buf[idx + 1] = (byte)((hash >> 8) & 0xFF);
                buf[idx + 2] = (byte)((hash >> 16) & 0xFF);
                buf[idx + 3] = 255;
            }
        }
        return buf;
    }

    private static ScreenFrame CropCanvas(byte[] canvas, int w, int yOffset, int height)
    {
        int stride = w * 4;
        byte[] buf = new byte[stride * height];
        Buffer.BlockCopy(canvas, yOffset * stride, buf, 0, buf.Length);
        return new ScreenFrame(new PxRect(0, 0, w, height), stride, buf);
    }

    private static byte[] CreateStaticFooterStrip(int w, int height)
    {
        int stride = w * 4;
        byte[] buf = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * stride + x * 4;
                buf[idx] = (byte)((x * 13 + 30) % 256);
                buf[idx + 1] = (byte)((x * 29 + y * 7) % 256);
                buf[idx + 2] = 220;
                buf[idx + 3] = 255;
            }
        }
        return buf;
    }

    private static ScreenFrame OverlayFooter(ScreenFrame src, byte[] footer, int footerRows)
    {
        int stride = src.Width * 4;
        byte[] buf = src.CopyRegionBytes(src.Bounds);
        Buffer.BlockCopy(footer, 0, buf, (src.Height - footerRows) * stride, footer.Length);
        return new ScreenFrame(src.Bounds, stride, buf);
    }

    private sealed class StubToolbarActions : ICaptureToolbarActions
    {
        public void SelectTool(AnnotationTool tool) { }
        public void Undo() { }
        public void Redo() { }
        public void Copy() { }
        public void Save() { }
        public void Pin() { }
        public void Longshot() { }
        public void Ocr() { }
        public void Translate() { }
        public void Accept() { }
        public void Cancel() { }
    }
}
