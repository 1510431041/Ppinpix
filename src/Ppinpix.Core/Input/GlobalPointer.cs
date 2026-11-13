using Ppinpix.Core.Geometry;
using Ppinpix.Core.Interop;

namespace Ppinpix.Core.Input;

/// <summary>
/// 全局光标位置。始终以物理像素返回，跨显示器拖动时也准确。
/// 名字刻意不叫 Cursor，避免与 System.Windows.Input.Cursor 冲突。
/// </summary>
public static class GlobalPointer
{
    public static bool TryGetPosition(out PxPoint point)
    {
        if (WindowApi.GetCursorPos(out var p))
        {
            point = new PxPoint(p.X, p.Y);
            return true;
        }

        point = PxPoint.Empty;
        return false;
    }

    public static PxPoint GetPosition() => TryGetPosition(out var p) ? p : PxPoint.Empty;

    public static void SetPosition(PxPoint point) => WindowApi.SetCursorPos(point.X, point.Y);
}
