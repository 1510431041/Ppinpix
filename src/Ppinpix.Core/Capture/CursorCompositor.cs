using System.Runtime.InteropServices;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Interop;

namespace Ppinpix.Core.Capture;

/// <summary>
/// 把鼠标指针合成进截图像素。
/// 只在光标周围开一小块 DIB，代价不随屏幕大小增长。
/// </summary>
public static class CursorCompositor
{
    private const int CURSOR_SHOWING = 0x0001;
    private const int DI_NORMAL = 0x0003;
    private const int Padding = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public nint hCursor;
        public DisplayApi.POINT ptScreenPos;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(nint hdc, int xLeft, int yTop, nint hIcon, int cxWidth, int cyHeight, int istepIfAniCur, nint hbrFlickerFreeDraw, int diFlags);

    /// <summary>把当前光标画到 <paramref name="frame"/> 上（原地修改）。光标不在帧内时什么都不做。</summary>
    public static void Composite(ScreenFrame frame)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info)) return;
        if ((info.flags & CURSOR_SHOWING) == 0 || info.hCursor == 0) return;

        var cursor = new PxPoint(info.ptScreenPos.X, info.ptScreenPos.Y);
        if (!frame.Bounds.Contains(cursor)) return;

        // 以光标为中心取一小块，确保图标（含热点偏移）完整落在块内。
        var patch = new PxRect(
            cursor.X - frame.Bounds.X - Padding,
            cursor.Y - frame.Bounds.Y - Padding,
            Padding * 2,
            Padding * 2).Intersect(new PxRect(0, 0, frame.Width, frame.Height));

        if (patch.IsEmpty) return;

        nint screenDc = 0;
        nint memoryDc = 0;
        nint dib = 0;
        nint previous = 0;

        try
        {
            screenDc = GdiApi.GetDC(0);
            if (screenDc == 0) return;
            memoryDc = GdiApi.CreateCompatibleDC(screenDc);
            if (memoryDc == 0) return;

            var bmi = new GdiApi.BITMAPINFO
            {
                bmiHeader = new GdiApi.BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<GdiApi.BITMAPINFOHEADER>(),
                    biWidth = patch.Width,
                    biHeight = -patch.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = GdiApi.BI_RGB,
                    biSizeImage = patch.Width * patch.Height * 4,
                },
                bmiColors = new uint[256],
            };

            dib = GdiApi.CreateDIBSection(memoryDc, ref bmi, GdiApi.DIB_RGB_COLORS, out nint bits, 0, 0);
            if (dib == 0 || bits == 0) return;
            previous = GdiApi.SelectObject(memoryDc, dib);

            int stride = patch.Width * 4;
            byte[] buffer = new byte[(long)stride * patch.Height];

            var pixels = frame.Pixels.Span;
            for (int y = 0; y < patch.Height; y++)
            {
                int srcOffset = (patch.Y + y) * frame.Stride + patch.X * 4;
                pixels.Slice(srcOffset, stride).CopyTo(buffer.AsSpan(y * stride, stride));
            }

            Marshal.Copy(buffer, 0, bits, buffer.Length);

            // 光标在 patch 内的位置：patch 是相对帧的，光标相对帧的位置已知。
            int localX = cursor.X - frame.Bounds.X - patch.X;
            int localY = cursor.Y - frame.Bounds.Y - patch.Y;
            DrawIconEx(memoryDc, localX, localY, info.hCursor, 0, 0, 0, 0, DI_NORMAL);

            Marshal.Copy(bits, buffer, 0, buffer.Length);
            for (int y = 0; y < patch.Height; y++)
            {
                int srcOffset = y * stride;
                int dstOffset = (patch.Y + y) * frame.Stride + patch.X * 4;
                buffer.AsSpan(srcOffset, stride).CopyTo(pixels[dstOffset..]);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("合成鼠标指针失败，截图将不含指针。", ex);
        }
        finally
        {
            if (previous != 0 && memoryDc != 0) GdiApi.SelectObject(memoryDc, previous);
            if (dib != 0) GdiApi.DeleteObject(dib);
            if (memoryDc != 0) GdiApi.DeleteDC(memoryDc);
            if (screenDc != 0) GdiApi.ReleaseDC(0, screenDc);
        }
    }
}
