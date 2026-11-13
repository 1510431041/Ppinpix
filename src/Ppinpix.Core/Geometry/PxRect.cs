namespace Ppinpix.Core.Geometry;

/// <summary>
/// 虚拟桌面物理像素坐标。原点为主显示器左上角，主显示器不在最左时 X/Y 可为负。
/// 全程序（截屏、选区、吸附、标注、贴图位置）统一使用该坐标系，绝不使用 DIP 存储。
/// </summary>
public readonly record struct PxPoint(int X, int Y)
{
    public static readonly PxPoint Empty = new(0, 0);

    public PxPoint Offset(int dx, int dy) => new(X + dx, Y + dy);

    public override string ToString() => $"{X},{Y}";
}

/// <summary>物理像素尺寸。</summary>
public readonly record struct PxSize(int Width, int Height)
{
    public static readonly PxSize Empty = new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"{Width} × {Height}";
}

/// <summary>物理像素矩形。半开区间 [Left, Right) × [Top, Bottom)。</summary>
public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public static readonly PxRect Empty = new(0, 0, 0, 0);

    public static PxRect FromLtrb(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>由任意两个角点构造，自动归一化（左/上取小）。</summary>
    public static PxRect FromCorners(PxPoint a, PxPoint b) => FromLtrb(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public PxPoint Location => new(X, Y);
    public PxPoint Center => new(X + Width / 2, Y + Height / 2);
    public PxSize Size => new(Width, Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public long AreaPx => (long)Math.Max(0, Width) * Math.Max(0, Height);

    public bool Contains(PxPoint p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public bool Contains(PxRect other) =>
        other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    public bool IntersectsWith(PxRect other) =>
        other.Left < Right && Left < other.Right && other.Top < Bottom && Top < other.Bottom;

    public PxRect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    public PxRect Offset(PxPoint delta) => Offset(delta.X, delta.Y);

    public PxRect Inflate(int delta) =>
        new(X - delta, Y - delta, Width + delta * 2, Height + delta * 2);

    public PxRect WithSize(PxSize size) => new(X, Y, size.Width, size.Height);

    public PxRect Intersect(PxRect other)
    {
        int l = Math.Max(Left, other.Left);
        int t = Math.Max(Top, other.Top);
        int r = Math.Min(Right, other.Right);
        int b = Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? Empty : FromLtrb(l, t, r, b);
    }

    public PxRect Union(PxRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromLtrb(
            Math.Min(Left, other.Left), Math.Min(Top, other.Top),
            Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    /// <summary>把矩形裁剪进 <paramref name="bounds"/> 内。</summary>
    public PxRect ClampTo(PxRect bounds) => Intersect(bounds);

    public override string ToString() => $"{X},{Y} {Width} × {Height}";
}
