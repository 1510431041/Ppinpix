using System.Runtime.InteropServices;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Interop;

namespace Ppinpix.Core.Capture;

/// <summary>
/// 一帧冻结的屏幕像素（BGRA32、自上而下、无预乘）。
/// 覆盖层、放大镜、马赛克/模糊取样、OCR、翻译发图全部以它为唯一像素来源。
/// </summary>
public sealed class ScreenFrame
{
    private readonly byte[] _pixels;

    public ScreenFrame(PxRect bounds, int stride, byte[] pixels)
    {
        if (stride < bounds.Width * 4)
            throw new ArgumentOutOfRangeException(nameof(stride), "跨距小于一行像素所需字节数。");
        if (pixels.Length < (long)stride * bounds.Height)
            throw new ArgumentException("像素缓冲区小于声明的尺寸。", nameof(pixels));

        Bounds = bounds;
        Stride = stride;
        _pixels = pixels;
    }

    /// <summary>该帧覆盖的虚拟桌面物理像素区域。</summary>
    public PxRect Bounds { get; }

    public int Stride { get; }

    public int Width => Bounds.Width;

    public int Height => Bounds.Height;

    /// <summary>像素缓冲区（BGRA32、自上而下）。可写，供合成鼠标指针、马赛克等原地修改使用。</summary>
    public Memory<byte> Pixels => _pixels;

    /// <summary>按绝对虚拟桌面坐标读取一个像素。越界返回透明黑。</summary>
    public void GetPixel(int x, int y, out byte b, out byte g, out byte r, out byte a)
    {
        int lx = x - Bounds.X;
        int ly = y - Bounds.Y;
        if ((uint)lx >= (uint)Width || (uint)ly >= (uint)Height)
        {
            b = g = r = a = 0;
            return;
        }

        int offset = ly * Stride + lx * 4;
        b = _pixels[offset];
        g = _pixels[offset + 1];
        r = _pixels[offset + 2];
        a = _pixels[offset + 3];
    }

    /// <summary>按绝对坐标裁剪出一块新帧；区域会被裁剪到本帧范围内。</summary>
    public ScreenFrame Crop(PxRect region)
    {
        var r = region.Intersect(Bounds);
        if (r.IsEmpty) return new ScreenFrame(PxRect.Empty, 0, []);

        int stride = r.Width * 4;
        byte[] buffer = new byte[(long)stride * r.Height];
        int srcStartX = r.X - Bounds.X;
        int srcStartY = r.Y - Bounds.Y;

        for (int row = 0; row < r.Height; row++)
        {
            int srcOffset = (srcStartY + row) * Stride + srcStartX * 4;
            Buffer.BlockCopy(_pixels, srcOffset, buffer, row * stride, stride);
        }

        return new ScreenFrame(r, stride, buffer);
    }

    /// <summary>按绝对坐标裁出一块紧打包的 BGRA 字节数组（用于编码 PNG / 上传模型）。</summary>
    public byte[] CopyRegionBytes(PxRect region)
    {
        var frame = Crop(region);
        if (frame.Bounds.IsEmpty) return [];
        return frame._pixels[..(frame.Stride * frame.Height)];
    }

    /// <summary>以指定源矩形与缩放块大小生成马赛克帧（块内取平均色）。</summary>
    public ScreenFrame Mosaic(PxRect region, int blockPx)
    {
        var r = region.Intersect(Bounds);
        if (r.IsEmpty || blockPx <= 1) return Crop(region);

        var result = Crop(region);
        var work = result._pixels;
        int w = result.Width;
        int h = result.Height;

        for (int by = 0; by < h; by += blockPx)
        {
            int bh = Math.Min(blockPx, h - by);
            for (int bx = 0; bx < w; bx += blockPx)
            {
                int bw = Math.Min(blockPx, w - bx);

                int sumB = 0, sumG = 0, sumR = 0, count = 0;
                for (int y = by; y < by + bh; y++)
                {
                    int offset = y * result.Stride + bx * 4;
                    for (int x = 0; x < bw; x++, offset += 4)
                    {
                        sumB += work[offset];
                        sumG += work[offset + 1];
                        sumR += work[offset + 2];
                        count++;
                    }
                }

                byte avgB = (byte)(sumB / count);
                byte avgG = (byte)(sumG / count);
                byte avgR = (byte)(sumR / count);

                for (int y = by; y < by + bh; y++)
                {
                    int offset = y * result.Stride + bx * 4;
                    for (int x = 0; x < bw; x++, offset += 4)
                    {
                        work[offset] = avgB;
                        work[offset + 1] = avgG;
                        work[offset + 2] = avgR;
                        work[offset + 3] = 255;
                    }
                }
            }
        }

        return result;
    }
}

/// <summary>抓屏接口。第一阶段的实现是 GDI BitBlt，后续会加入 DXGI Desktop Duplication。</summary>
public interface IScreenCapture
{
    /// <summary>抓取指定虚拟桌面区域。区域必须落在虚拟桌面范围内。</summary>
    ScreenFrame Capture(PxRect region);
}

/// <summary>
/// 基于 GDI BitBlt 的抓屏实现。
/// 兼容性最好、无需第三方依赖；硬件覆盖层（部分视频播放器、独占全屏游戏）可能抓成黑屏，
/// 这类场景由后续的 DXGI 实现兜住。
/// </summary>
public sealed class GdiScreenCapture : IScreenCapture
{
    public ScreenFrame Capture(PxRect region)
    {
        if (region.IsEmpty)
            throw new ArgumentException("抓取区域为空。", nameof(region));

        var bounds = region;

        nint screenDc = 0;
        nint memoryDc = 0;
        nint dib = 0;
        nint previous = 0;

        try
        {
            screenDc = GdiApi.GetDC(0);
            if (screenDc == 0) throw new InvalidOperationException("GetDC(NULL) 失败。");

            memoryDc = GdiApi.CreateCompatibleDC(screenDc);
            if (memoryDc == 0) throw new InvalidOperationException("CreateCompatibleDC 失败。");

            var info = new GdiApi.BITMAPINFO
            {
                bmiHeader = new GdiApi.BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<GdiApi.BITMAPINFOHEADER>(),
                    biWidth = bounds.Width,
                    // 负高度 = 自上而下的行序，与 ScreenFrame 约定一致。
                    biHeight = -bounds.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = GdiApi.BI_RGB,
                    biSizeImage = bounds.Width * bounds.Height * 4,
                },
                bmiColors = new uint[256],
            };

            dib = GdiApi.CreateDIBSection(screenDc, ref info, GdiApi.DIB_RGB_COLORS, out nint bits, 0, 0);
            if (dib == 0 || bits == 0) throw new InvalidOperationException("CreateDIBSection 失败。");

            previous = GdiApi.SelectObject(memoryDc, dib);

            bool ok = GdiApi.BitBlt(
                memoryDc, 0, 0, bounds.Width, bounds.Height,
                screenDc, bounds.X, bounds.Y,
                GdiApi.SRCCOPY | GdiApi.CAPTUREBLT);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"BitBlt 抓屏失败，Win32 错误码 {err}。");
            }

            int stride = bounds.Width * 4;
            byte[] pixels = new byte[(long)stride * bounds.Height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            // BitBlt 不写 alpha 通道，统一置为不透明，避免后续绘制出现全透明图。
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            return new ScreenFrame(bounds, stride, pixels);
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
