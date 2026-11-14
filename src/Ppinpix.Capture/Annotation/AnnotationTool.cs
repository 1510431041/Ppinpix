namespace Ppinpix.Capture.Annotation;

/// <summary>截图编辑阶段的标注工具类型。</summary>
public enum AnnotationTool
{
    /// <summary>无工具 / 默认选区操作（选择、移动、缩放标注或调整选区）。</summary>
    None = 0,

    /// <summary>选择与编辑已有标注。</summary>
    Select = 1,

    /// <summary>矩形（快捷键 R）。</summary>
    Rectangle = 2,

    /// <summary>椭圆（快捷键 O）。</summary>
    Ellipse = 3,

    /// <summary>箭头（快捷键 A）。</summary>
    Arrow = 4,

    /// <summary>直线（快捷键 L）。</summary>
    Line = 5,

    /// <summary>画笔自由曲线（快捷键 P）。</summary>
    Freehand = 6,

    /// <summary>序号圆圈 1, 2, 3…（快捷键 N）。</summary>
    NumberBadge = 7,

    /// <summary>文字标注（快捷键 T）。</summary>
    Text = 8,

    /// <summary>马赛克遮挡（快捷键 M）。</summary>
    Mosaic = 9,

    /// <summary>高斯模糊遮挡。</summary>
    Blur = 10,

    /// <summary>荧光笔半透明高亮（快捷键 H）。</summary>
    Highlight = 11,

    /// <summary>橡皮擦（点击或划过标注删除）。</summary>
    Eraser = 12,

    /// <summary>聚光灯（暗化选区其余部分，保留聚光区域原始亮度，快捷键 K）。</summary>
    Spotlight = 13,
}
