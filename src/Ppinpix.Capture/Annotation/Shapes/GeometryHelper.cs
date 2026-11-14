using SkiaSharp;

namespace Ppinpix.Capture.Annotation.Shapes;

/// <summary>几何计算、控制点缩放、箭头头部三角与 Skia 画笔工厂。</summary>
public static class GeometryHelper
{
    public static double Distance(double x1, double y1, double x2, double y2) =>
        double.Hypot(x2 - x1, y2 - y1);

    public static double DistancePointToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-6) return double.Hypot(px - x1, py - y1);

        double t = ((px - x1) * dx + (py - y1) * dy) / lenSq;
        t = Math.Clamp(t, 0.0, 1.0);
        double projX = x1 + t * dx;
        double projY = y1 + t * dy;
        return double.Hypot(px - projX, py - projY);
    }

    /// <summary>Shift 约束：将拖动点规范为以锚点为基准的正方形。</summary>
    public static (double X, double Y) ConstrainSquare(double anchorX, double anchorY, double x, double y)
    {
        double dx = x - anchorX;
        double dy = y - anchorY;
        double size = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return (
            anchorX + (dx >= 0 ? size : -size),
            anchorY + (dy >= 0 ? size : -size));
    }

    /// <summary>Shift 约束：将线段吸附到最近的 45 度角。</summary>
    public static (double X, double Y) ConstrainAngle45(double anchorX, double anchorY, double x, double y)
    {
        double dx = x - anchorX;
        double dy = y - anchorY;
        double len = double.Hypot(dx, dy);
        if (len <= 1e-6) return (x, y);

        double angle = Math.Atan2(dy, dx);
        double step = Math.PI / 4.0;
        double snapped = Math.Round(angle / step) * step;
        return (anchorX + len * Math.Cos(snapped), anchorY + len * Math.Sin(snapped));
    }

    public static IReadOnlyList<ShapeHandle> GetBoxHandles(RectF r) =>
    [
        new(ShapeHandleKind.TopLeft, r.Left, r.Top),
        new(ShapeHandleKind.Top, r.CenterX, r.Top),
        new(ShapeHandleKind.TopRight, r.Right, r.Top),
        new(ShapeHandleKind.Right, r.Right, r.CenterY),
        new(ShapeHandleKind.BottomRight, r.Right, r.Bottom),
        new(ShapeHandleKind.Bottom, r.CenterX, r.Bottom),
        new(ShapeHandleKind.BottomLeft, r.Left, r.Bottom),
        new(ShapeHandleKind.Left, r.Left, r.CenterY),
    ];

    public static double ClampCornerRadius(RectF r, double requestedRadius) =>
        Math.Clamp(requestedRadius, 0, Math.Min(r.Width, r.Height) * 0.5);

    public static IReadOnlyList<ShapeHandle> GetBoxHandlesWithCornerRadius(RectF r, double cornerRadius)
    {
        var list = new List<ShapeHandle>(9)
        {
            new(ShapeHandleKind.TopLeft, r.Left, r.Top),
            new(ShapeHandleKind.Top, r.CenterX, r.Top),
            new(ShapeHandleKind.TopRight, r.Right, r.Top),
            new(ShapeHandleKind.Right, r.Right, r.CenterY),
            new(ShapeHandleKind.BottomRight, r.Right, r.Bottom),
            new(ShapeHandleKind.Bottom, r.CenterX, r.Bottom),
            new(ShapeHandleKind.BottomLeft, r.Left, r.Bottom),
            new(ShapeHandleKind.Left, r.Left, r.CenterY),
        };
        if (r.Width >= 60 && r.Height >= 48)
        {
            double clamped = ClampCornerRadius(r, cornerRadius);
            double hx = Math.Clamp(r.Left + 16 + clamped * 0.65, r.Left + 16, r.CenterX - 10);
            list.Add(new(ShapeHandleKind.CornerRadius, hx, r.Top + 14));
        }
        return list;
    }

    public static double ComputeCornerRadiusFromHandle(RectF r, double x)
    {
        double maxR = Math.Min(r.Width, r.Height) * 0.5;
        double raw = Math.Clamp((x - r.Left - 16) / 0.65, 0, maxR);
        return raw < 2.0 ? 0 : Math.Round(raw);
    }

    public static double DistancePointToQuadraticBezier(
        double px, double py,
        double x1, double y1,
        double cx, double cy,
        double x2, double y2,
        int segments = 16)
    {
        double minDist = double.MaxValue;
        double prevX = x1;
        double prevY = y1;
        for (int i = 1; i <= segments; i++)
        {
            double t = (double)i / segments;
            double mt = 1.0 - t;
            double nx = mt * mt * x1 + 2.0 * mt * t * cx + t * t * x2;
            double ny = mt * mt * y1 + 2.0 * mt * t * cy + t * t * y2;
            double d = DistancePointToSegment(px, py, prevX, prevY, nx, ny);
            if (d < minDist) minDist = d;
            prevX = nx;
            prevY = ny;
        }
        return minDist;
    }

    public static RectF MoveBoxHandle(RectF origin, ShapeHandleKind kind, double x, double y, bool constrain, double minSize = 4)
    {
        double left = origin.Left;
        double top = origin.Top;
        double right = origin.Right;
        double bottom = origin.Bottom;

        switch (kind)
        {
            case ShapeHandleKind.TopLeft:
                left = Math.Min(x, right - minSize);
                top = Math.Min(y, bottom - minSize);
                if (constrain)
                {
                    double s = Math.Max(right - left, bottom - top);
                    left = right - s;
                    top = bottom - s;
                }
                break;
            case ShapeHandleKind.Top:
                top = Math.Min(y, bottom - minSize);
                break;
            case ShapeHandleKind.TopRight:
                right = Math.Max(x, left + minSize);
                top = Math.Min(y, bottom - minSize);
                if (constrain)
                {
                    double s = Math.Max(right - left, bottom - top);
                    right = left + s;
                    top = bottom - s;
                }
                break;
            case ShapeHandleKind.Right:
                right = Math.Max(x, left + minSize);
                break;
            case ShapeHandleKind.BottomRight:
                right = Math.Max(x, left + minSize);
                bottom = Math.Max(y, top + minSize);
                if (constrain)
                {
                    double s = Math.Max(right - left, bottom - top);
                    right = left + s;
                    bottom = top + s;
                }
                break;
            case ShapeHandleKind.Bottom:
                bottom = Math.Max(y, top + minSize);
                break;
            case ShapeHandleKind.BottomLeft:
                left = Math.Min(x, right - minSize);
                bottom = Math.Max(y, top + minSize);
                if (constrain)
                {
                    double s = Math.Max(right - left, bottom - top);
                    left = right - s;
                    bottom = top + s;
                }
                break;
            case ShapeHandleKind.Left:
                left = Math.Min(x, right - minSize);
                break;
        }

        return new RectF(left, top, Math.Max(minSize, right - left), Math.Max(minSize, bottom - top));
    }

    public static (PointF Tip, PointF LeftWing, PointF RightWing, PointF Notch, PointF ShaftEnd) ComputeArrowGeometry(
        double x1, double y1, double x2, double y2, double lineWidth)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double len = double.Hypot(dx, dy);
        if (len <= 1e-4)
        {
            var p = new PointF(x2, y2);
            return (p, p, p, p, p);
        }

        double ux = dx / len;
        double uy = dy / len;
        double headLen = Math.Min(len * 0.7, Math.Max(13.0, lineWidth * 4.5));
        double halfWidth = headLen * 0.48;
        double notchBack = headLen * 0.78;

        double baseX = x2 - ux * headLen;
        double baseY = y2 - uy * headLen;
        double perpX = -uy * halfWidth;
        double perpY = ux * halfWidth;

        var tip = new PointF(x2, y2);
        var left = new PointF(baseX + perpX, baseY + perpY);
        var right = new PointF(baseX - perpX, baseY - perpY);
        var notch = new PointF(x2 - ux * notchBack, y2 - uy * notchBack);
        var shaftEnd = new PointF(x2 - ux * Math.Max(0, notchBack - lineWidth * 0.35), y2 - uy * Math.Max(0, notchBack - lineWidth * 0.35));
        return (tip, left, right, notch, shaftEnd);
    }

    public static bool PointInTriangle(double px, double py, PointF a, PointF b, PointF c)
    {
        static double Sign(double x1, double y1, double x2, double y2, double x3, double y3) =>
            (x1 - x3) * (y2 - y3) - (x2 - x3) * (y1 - y3);

        double d1 = Sign(px, py, a.X, a.Y, b.X, b.Y);
        double d2 = Sign(px, py, b.X, b.Y, c.X, c.Y);
        double d3 = Sign(px, py, c.X, c.Y, a.X, a.Y);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
        return !(hasNeg && hasPos);
    }

    public static SKColor ToSkColor(uint argb) =>
        new((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF), (byte)((argb >> 24) & 0xFF));

    public static SKPaint CreateStrokePaint(ShapeStyle style)
    {
        float stroke = (float)Math.Max(1.0, style.LineWidth);
        var paint = new SKPaint
        {
            Color = ToSkColor(style.Argb),
            StrokeWidth = stroke,
            Style = style.Filled ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        if (style.Dashed && !style.Filled)
        {
            float dash = Math.Max(4f, stroke * 2.5f);
            float gap = Math.Max(3f, stroke * 1.8f);
            paint.PathEffect = SKPathEffect.CreateDash([dash, gap], 0);
        }
        return paint;
    }

    public static bool IsLightColor(uint argb)
    {
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
        return lum > 0.68;
    }

    public static void DrawClippedPatch(
        SKCanvas canvas,
        SKBitmap patch,
        Core.Geometry.PxRect clamped,
        RectF shapeRect,
        double cornerRadius,
        double originX,
        double originY)
    {
        float radius = (float)ClampCornerRadius(shapeRect, cornerRadius);
        float px = (float)(clamped.X - originX);
        float py = (float)(clamped.Y - originY);
        if (radius > 0.5f)
        {
            var clipRect = SKRect.Create(
                (float)(shapeRect.X - originX),
                (float)(shapeRect.Y - originY),
                (float)shapeRect.Width,
                (float)shapeRect.Height);
            canvas.Save();
            using var rr = new SKRoundRect(clipRect, radius, radius);
            canvas.ClipRoundRect(rr, SKClipOperation.Intersect, antialias: true);
            canvas.DrawBitmap(patch, px, py);
            canvas.Restore();
        }
        else
        {
            canvas.DrawBitmap(patch, px, py);
        }
    }
}
