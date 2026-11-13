using Ppinpix.Capture.Annotation;
using Ppinpix.Capture.Annotation.Shapes;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Xunit;

namespace Ppinpix.Capture.Tests;

public sealed class AnnotationEditorTests
{
    [Fact]
    public void Badges_UndoRedoAndEraser_ShouldRenumberConsistently()
    {
        using var editor = new AnnotationEditor(new AnnotationSettings());
        editor.SetTool(AnnotationTool.NumberBadge);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(editor.OnPointerDown(20 + i * 60, 40, 8));
            Assert.True(editor.OnPointerUp(20 + i * 60, 40, false));
        }

        Assert.Equal(3, editor.Shapes.Count);
        Assert.Equal([1, 2, 3], editor.Shapes.OfType<NumberBadgeShape>().Select(b => b.Number).ToArray());

        // 擦除中间 #2 后，#3 自动重排为 #2
        editor.SetTool(AnnotationTool.Eraser);
        Assert.True(editor.OnPointerDown(80, 40, 12));
        Assert.True(editor.OnPointerUp(80, 40, false));

        Assert.Equal(2, editor.Shapes.Count);
        Assert.Equal([1, 2], editor.Shapes.OfType<NumberBadgeShape>().Select(b => b.Number).ToArray());

        // 撤销后恢复 #1, #2, #3
        editor.Undo();
        Assert.Equal(3, editor.Shapes.Count);
        Assert.Equal([1, 2, 3], editor.Shapes.OfType<NumberBadgeShape>().Select(b => b.Number).ToArray());

        editor.Redo();
        Assert.Equal([1, 2], editor.Shapes.OfType<NumberBadgeShape>().Select(b => b.Number).ToArray());
    }

    [Fact]
    public void SelectMoveAndResize_RectangleShouldSupportUndoRedo()
    {
        using var editor = new AnnotationEditor(new AnnotationSettings());
        editor.SetTool(AnnotationTool.Rectangle);

        editor.OnPointerDown(10, 10, 6);
        editor.OnPointerMove(110, 90, false, 6);
        editor.OnPointerUp(110, 90, false);

        var rectShape = Assert.IsType<RectangleShape>(editor.Shapes.Single());
        var origBounds = rectShape.Bounds;
        Assert.Equal(100, origBounds.Width, 1);

        // 1) 拖动上边框（避开顶点与正中控制点，命中整个边框平移）
        editor.SetTool(AnnotationTool.Select);
        Assert.True(editor.OnPointerDown(35, 10, 6));
        Assert.True(editor.OnPointerMove(55, 25, false, 6));
        Assert.True(editor.OnPointerUp(55, 25, false));

        Assert.Equal(origBounds.X + 20, editor.Shapes.Single().Bounds.X, 1);
        Assert.Equal(origBounds.Y + 15, editor.Shapes.Single().Bounds.Y, 1);

        editor.Undo();
        Assert.Equal(origBounds.X, editor.Shapes.Single().Bounds.X, 1);
        Assert.Equal(origBounds.Y, editor.Shapes.Single().Bounds.Y, 1);

        // 2) 拖动左侧控制点缩放并撤销
        Assert.True(editor.OnPointerDown(10, 50, 6));
        Assert.True(editor.OnPointerMove(30, 50, false, 6));
        Assert.True(editor.OnPointerUp(30, 50, false));
        Assert.Equal(80, editor.Shapes.Single().Bounds.Width, 1);

        editor.Undo();
        Assert.Equal(origBounds.X, editor.Shapes.Single().Bounds.X, 1);
        Assert.Equal(100, editor.Shapes.Single().Bounds.Width, 1);
    }

    [Fact]
    public void RenderToFrame_MosaicShouldSampleSourcePixelsWithoutAlteringOutsidePixels()
    {
        // 创建 64x64 棋盘图案原帧（一半纯红、一半纯蓝）
        const int w = 64, h = 64;
        int stride = w * 4;
        byte[] px = new byte[stride * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int off = y * stride + x * 4;
                bool isRed = ((x / 4) + (y / 4)) % 2 == 0;
                px[off] = isRed ? (byte)0 : (byte)255;     // B
                px[off + 1] = 0;                             // G
                px[off + 2] = isRed ? (byte)255 : (byte)0;   // R
                px[off + 3] = 255;                           // A
            }
        }
        var frame = new ScreenFrame(new PxRect(0, 0, w, h), stride, px);

        using var editor = new AnnotationEditor(new AnnotationSettings { DefaultMosaicBlockPx = 12 });
        editor.SetTool(AnnotationTool.Mosaic);
        editor.OnPointerDown(8, 8, 6);
        editor.OnPointerMove(32, 32, false, 6);
        editor.OnPointerUp(32, 32, false);

        var rendered = editor.RenderToFrame(frame, frame.Bounds);
        Assert.Equal(w, rendered.Width);
        Assert.Equal(h, rendered.Height);

        // 马赛克区域外像素 (50, 50) 应与原帧 1:1 完全一致
        frame.GetPixel(50, 50, out byte ob, out byte og, out byte or_, out byte oa);
        rendered.GetPixel(50, 50, out byte rb, out byte rg, out byte rr, out byte ra);
        Assert.Equal((ob, og, or_, oa), (rb, rg, rr, ra));

        // 马赛克区域内 [12..24) 块内采样值应均一化
        rendered.GetPixel(13, 13, out byte mb1, out _, out byte mr1, out _);
        rendered.GetPixel(15, 19, out byte mb2, out _, out byte mr2, out _);
        Assert.Equal(mb1, mb2);
        Assert.Equal(mr1, mr2);
    }
}
