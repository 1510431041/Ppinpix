namespace Ppinpix.Core;

/// <summary>
/// 功能开关。截图工具栏按这些开关决定显示哪些按钮，
/// 未实现的模块不会在界面上留下点了没反应的入口。
///
/// 刻意用 static readonly 而不是 const：const false 会让编译器把
/// <c>if (Features.Pin)</c> 的分支判定为“无法访问的代码”并报 CS0162 警告，
/// 而这段代码正是要留给后续里程碑打开用的。
/// </summary>
public static class Features
{
    /// <summary>悬浮贴图（M2）。</summary>
    public static readonly bool Pin = true;

    /// <summary>离线 OCR 与二维码（M3）。</summary>
    public static readonly bool Ocr = true;

    /// <summary>大模型截图翻译（M4）。</summary>
    public static readonly bool Translate = true;

    /// <summary>标注工具（M2）。</summary>
    public static readonly bool Annotate = true;
}
