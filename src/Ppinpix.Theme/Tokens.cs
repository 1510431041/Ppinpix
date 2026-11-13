using System.Windows.Media;

namespace Ppinpix.Theme;

/// <summary>
/// 视觉规范（docs/UI_VISUAL.md）的代码化版本。
/// 所有界面尺寸、颜色、动效时长都必须从这里取，禁止在控件里硬编码。
/// </summary>
public static class Tokens
{
    // ---------- 品牌色 ----------
    public static readonly Color Accent = Color.FromRgb(0x2F, 0x80, 0xFF);
    public static readonly Color AccentHover = Color.FromRgb(0x5A, 0x9B, 0xFF);
    public static readonly Color AccentPressed = Color.FromRgb(0x1F, 0x66, 0xD9);
    public static readonly Color OnAccent = Colors.White;

    // ---------- 浮层（深色） ----------
    public static readonly Color Surface = Color.FromRgb(0x1F, 0x23, 0x29);
    public static readonly Color SurfaceRaised = Color.FromRgb(0x2A, 0x2F, 0x37);
    public static readonly Color TextPrimary = Color.FromRgb(0xF2, 0xF4, 0xF7);
    public static readonly Color TextSecondary = Color.FromRgb(0xA3, 0xAB, 0xB8);
    public static readonly Color TextDisabled = Color.FromRgb(0x5C, 0x63, 0x70);

    // ---------- 语义色 ----------
    public static readonly Color Success = Color.FromRgb(0x2E, 0xB8, 0x72);
    public static readonly Color Warning = Color.FromRgb(0xF5, 0xA5, 0x24);
    public static readonly Color Danger = Color.FromRgb(0xF0, 0x44, 0x38);

    // ---------- 带透明度的叠色 ----------
    public const byte SurfaceAlpha = 240;      // 94%
    public const byte StrokeAlpha = 26;        // 10%
    public const byte HoverAlpha = 20;         // 8%
    public const byte PressedAlpha = 36;       // 14%
    public const byte AccentSoftAlpha = 46;    // 18%
    public const byte DimAlpha = 115;          // 45%
    public const byte GridLineAlpha = 31;      // 12%

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static readonly Color SurfaceOpaque = WithAlpha(Surface, SurfaceAlpha);
    public static readonly Color Stroke = WithAlpha(Colors.White, StrokeAlpha);
    public static readonly Color SurfaceHover = WithAlpha(Colors.White, HoverAlpha);
    public static readonly Color SurfacePressed = WithAlpha(Colors.White, PressedAlpha);
    public static readonly Color AccentSoft = WithAlpha(Accent, AccentSoftAlpha);
    public static readonly Color Dim = WithAlpha(Colors.Black, DimAlpha);
    public static readonly Color GridLine = WithAlpha(Colors.White, GridLineAlpha);

    // ---------- 设置窗口（跟随系统浅/深色） ----------
    public static class Light
    {
        public static readonly Color WindowBg = Color.FromRgb(0xF5, 0xF6, 0xF8);
        public static readonly Color CardBg = Colors.White;
        public static readonly Color CardStroke = Color.FromRgb(0xE3, 0xE6, 0xEB);
        public static readonly Color NavSelectedBg = WithAlpha(Accent, 31);
        public static readonly Color TextPrimary = Color.FromRgb(0x1B, 0x1F, 0x24);
        public static readonly Color TextSecondary = Color.FromRgb(0x5F, 0x6B, 0x7A);
    }

    public static class Dark
    {
        public static readonly Color WindowBg = Color.FromRgb(0x19, 0x1C, 0x21);
        public static readonly Color CardBg = Color.FromRgb(0x22, 0x26, 0x2D);
        public static readonly Color CardStroke = WithAlpha(Colors.White, 20);
        public static readonly Color NavSelectedBg = WithAlpha(Accent, 56);
        public static readonly Color TextPrimary = Color.FromRgb(0xF2, 0xF4, 0xF7);
        public static readonly Color TextSecondary = Color.FromRgb(0xA3, 0xAB, 0xB8);
    }

    // ---------- 标注默认调色板（顺序固定，工具栏色块按此顺序） ----------
    public static readonly Color[] AnnotationPalette =
    [
        Color.FromRgb(0xF0, 0x44, 0x38), // 红（默认）
        Color.FromRgb(0xF5, 0xA5, 0x24), // 橙
        Color.FromRgb(0xFA, 0xDB, 0x14), // 黄
        Color.FromRgb(0x2E, 0xB8, 0x72), // 绿
        Color.FromRgb(0x2F, 0x80, 0xFF), // 蓝
        Color.FromRgb(0x9B, 0x5D, 0xE5), // 紫
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 白
        Color.FromRgb(0x1B, 0x1F, 0x24), // 黑
    ];

    // ---------- 尺寸 ----------
    public const double CornerPanel = 8;
    public const double CornerControl = 6;
    public const double CornerBadge = 4;

    public const double ControlHeight = 32;
    public const double ToolButtonSize = 32;
    public const double ToolbarHeight = 40;
    public const double ListItemHeight = 36;
    public const double NavItemHeight = 40;

    public const double Spacing1 = 4;
    public const double Spacing2 = 8;
    public const double Spacing3 = 12;
    public const double Spacing4 = 16;
    public const double Spacing5 = 24;
    public const double Spacing6 = 32;

    public const double IconSize = 16;
    public const double MinHitSize = 32;

    /// <summary>浮层阴影（0 8 24 rgba(0,0,0,.35)）。</summary>
    public const double ShadowBlur = 24;
    public const double ShadowDepth = 8;
    public const double ShadowOpacity = 0.35;

    /// <summary>贴图阴影（0 4 16 rgba(0,0,0,.30)）。</summary>
    public const double PinShadowBlur = 16;
    public const double PinShadowDepth = 4;
    public const double PinShadowOpacity = 0.30;

    // ---------- 动效 ----------
    public static readonly TimeSpan OverlayInDuration = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan OverlayOutDuration = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan ToastDuration = TimeSpan.FromMilliseconds(160);

    // ---------- 字体 ----------
    public const string UiFontFamily = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";
    public const string MonoFontFamily = "Cascadia Mono, Consolas, Courier New";
    public const string IconFontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public const double FontTitle = 20;
    public const double FontSubtitle = 15;
    public const double FontBody = 13;
    public const double FontCaption = 12;
    public const double FontBadge = 11;
    public const double FontMono = 12;
}

/// <summary>
/// 图标字形。使用系统自带的 Segoe Fluent Icons（Win11），Win10 回退 Segoe MDL2 Assets。
/// 界面里只引用常量名，不要直接写字形字符。
/// 码点取自 Segoe MDL2 Assets 字形表；新增图标前请在应用里目视确认一次。
/// </summary>
public static class Icons
{
    private static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);

    public const int Camera = 0xE722;
    public const int CropShape = 0xE7A8;
    public const int SaveDisk = 0xE74E;
    public const int CopyDoc = 0xE8C8;
    public const int PinMark = 0xE718;
    public const int UnpinMark = 0xE77A;
    public const int CloseMark = 0xE8BB;
    public const int AcceptMark = 0xE8FB;
    public const int UndoMark = 0xE7A7;
    public const int RedoMark = 0xE7A6;
    public const int SettingsGear = 0xE713;
    public const int EditPen = 0xE70F;
    public const int Highlighter = 0xE7E6;
    public const int EraserMark = 0xE75C;
    public const int GlobeMark = 0xE774;
    public const int DocMark = 0xE8A5;
    public const int ZoomMark = 0xE71E;
    public const int DeleteMark = 0xE74D;
    public const int ListMark = 0xE8FD;
    public const int ArrowMark = 0xE72A;
    public const int InfoMark = 0xE946;
    public const int WarningMark = 0xE7BA;
    public const int ErrorMark = 0xE783;
    public const int SuccessMark = 0xE930;
    public const int ChevronDownMark = 0xE70D;
    public const int FolderMark = 0xE8B7;
    public const int ViewMark = 0xE890;
    public const int KeyboardMark = 0xE765;
    public const int RotateMark = 0xE7AD;
    public const int FlipHMark = 0xE745;
    public const int FlipVMark = 0xE74B;
    public const int ShieldMark = 0xE72E;
    public const int RefreshMark = 0xE72C;
    public const int AddMark = 0xE710;
    public const int StopMark = 0xE71A;
    public const int HideMark = 0xED1A;

    public static readonly string Capture = Glyph(Camera);
    public static readonly string Crop = Glyph(CropShape);
    public static readonly string Save = Glyph(SaveDisk);
    public static readonly string Copy = Glyph(CopyDoc);
    public static readonly string Pin = Glyph(PinMark);
    public static readonly string Unpin = Glyph(UnpinMark);
    public static readonly string Close = Glyph(CloseMark);
    public static readonly string Accept = Glyph(AcceptMark);
    public static readonly string Undo = Glyph(UndoMark);
    public static readonly string Redo = Glyph(RedoMark);
    public static readonly string Settings = Glyph(SettingsGear);
    public static readonly string Edit = Glyph(EditPen);
    public static readonly string Highlight = Glyph(Highlighter);
    public static readonly string Eraser = Glyph(EraserMark);
    public static readonly string Globe = Glyph(GlobeMark);
    public static readonly string Document = Glyph(DocMark);
    public static readonly string Zoom = Glyph(ZoomMark);
    public static readonly string Delete = Glyph(DeleteMark);
    public static readonly string List = Glyph(ListMark);
    public static readonly string Arrow = Glyph(ArrowMark);
    public static readonly string Info = Glyph(InfoMark);
    public static readonly string Warning = Glyph(WarningMark);
    public static readonly string Error = Glyph(ErrorMark);
    public static readonly string Success = Glyph(SuccessMark);
    public static readonly string ChevronDown = Glyph(ChevronDownMark);
    public static readonly string Folder = Glyph(FolderMark);
    public static readonly string View = Glyph(ViewMark);
    public static readonly string Keyboard = Glyph(KeyboardMark);
    public static readonly string Rotate = Glyph(RotateMark);
    public static readonly string FlipH = Glyph(FlipHMark);
    public static readonly string FlipV = Glyph(FlipVMark);
    public static readonly string Shield = Glyph(ShieldMark);
    public static readonly string Refresh = Glyph(RefreshMark);
    public static readonly string Add = Glyph(AddMark);
    public static readonly string Stop = Glyph(StopMark);
    public static readonly string Hide = Glyph(HideMark);
}