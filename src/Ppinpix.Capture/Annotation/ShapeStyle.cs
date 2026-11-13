using System.Globalization;

namespace Ppinpix.Capture.Annotation;

/// <summary>单个标注图形的不可变样式快照，便于命令模式无副作用撤销/重做。</summary>
public sealed record ShapeStyle
{
    /// <summary>主色（ARGB 32 位，如 0xFFF04438）。</summary>
    public uint Argb { get; init; } = 0xFFF04438;

    /// <summary>线宽（物理像素，常规 2 / 3 / 5，荧光笔内部会按倍率放大）。</summary>
    public double LineWidth { get; init; } = 3;

    /// <summary>是否为实心填充（矩形/椭圆适用）。</summary>
    public bool Filled { get; init; }

    /// <summary>是否为虚线描边（矩形/椭圆/直线适用）。</summary>
    public bool Dashed { get; init; }

    /// <summary>字号（物理像素基准：14 / 18 / 24 / 32）。</summary>
    public double FontSize { get; init; } = 18;

    /// <summary>文字是否加粗。</summary>
    public bool Bold { get; init; }

    /// <summary>文字是否带半透明底框。</summary>
    public bool TextBackground { get; init; }

    /// <summary>文字字体名。</summary>
    public string FontFamily { get; init; } = "Microsoft YaHei UI";

    /// <summary>序号圆圈直径（20 / 24 / 32）。</summary>
    public int BadgeSize { get; init; } = 24;

    /// <summary>马赛克块尺寸（8 / 12 / 16 物理像素）。</summary>
    public int MosaicBlockPx { get; init; } = 12;

    /// <summary>高斯模糊半径（6 / 12 / 20）。</summary>
    public int BlurRadius { get; init; } = 12;

    public static ShapeStyle FromSettings(Ppinpix.Core.Settings.AnnotationSettings s) => new()
    {
        Argb = ParseHexColor(s.DefaultColorHex, 0xFFF04438),
        LineWidth = Math.Clamp(s.DefaultLineWidth, 1, 24),
        FontSize = Math.Clamp(s.DefaultFontSize, 8, 96),
        BadgeSize = Math.Clamp(s.DefaultBadgeSize, 16, 48),
        MosaicBlockPx = Math.Clamp(s.DefaultMosaicBlockPx, 4, 48),
        BlurRadius = Math.Clamp(s.DefaultBlurRadius, 2, 48),
    };

    public static uint ParseHexColor(string? hex, uint fallback = 0xFFF04438)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        string s = hex.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return 0xFF000000u | rgb;
        }
        if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            return argb;
        }
        return fallback;
    }

    public static string ToHex(uint argb) => $"#{(argb & 0x00FFFFFFu):X6}";

    public byte A => (byte)((Argb >> 24) & 0xFF);
    public byte R => (byte)((Argb >> 16) & 0xFF);
    public byte G => (byte)((Argb >> 8) & 0xFF);
    public byte B => (byte)(Argb & 0xFF);
}
