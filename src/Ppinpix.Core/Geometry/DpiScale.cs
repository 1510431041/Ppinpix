namespace Ppinpix.Core.Geometry;

/// <summary>DIP（96 DPI 逻辑像素）与物理像素之间的换算，绑定到某一块显示器的缩放比例。</summary>
public readonly record struct DpiScale(uint Dpi)
{
    public static readonly DpiScale Default = new(96);

    public double Scale => Dpi / 96.0;

    public double ToPx(double dip) => dip * Scale;

    public double ToDip(double px) => px / Scale;

    public int ToPxRounded(double dip) => (int)Math.Round(dip * Scale, MidpointRounding.AwayFromZero);

    /// <summary>以像素为单位、以给定点为中心对齐到整数像素，避免半像素导致位图发虚。</summary>
    public static double SnapToPixel(double px) => Math.Round(px);
}
