using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;

namespace Ppinpix.Capture.Longshot;

public enum StitchStatus
{
    Initialized = 0,
    Stitched = 1,
    NoMovement = 2,
    MatchFailed = 3,
    MaxHeightReached = 4,
}

public readonly record struct StitchStepResult(StitchStatus Status, int ShiftPx, int TotalHeightPx);

/// <summary>
/// 垂直滚动长截图拼接器（支持静态吸顶导航栏与固定底栏识别，采用高纹理模板带 + 全重叠验证）。
/// 输入与输出均为 BGRA32 紧打包像素帧。
/// </summary>
public sealed class LongshotStitcher
{
    public const int DefaultMaxHeightPx = 24_000;

    private readonly int _width;
    private readonly int _viewportHeight;
    private readonly int _stride;
    private readonly int _maxHeightPx;

    private byte[] _buffer;
    private int _currentHeight;
    private byte[] _lastFramePixels;

    public LongshotStitcher(ScreenFrame initialFrame, int maxHeightPx = DefaultMaxHeightPx)
    {
        ArgumentNullException.ThrowIfNull(initialFrame);
        if (initialFrame.Width <= 0 || initialFrame.Height <= 0)
            throw new ArgumentException("初始帧尺寸必须大于 0。", nameof(initialFrame));

        _width = initialFrame.Width;
        _viewportHeight = initialFrame.Height;
        _stride = _width * 4;
        _maxHeightPx = Math.Max(_viewportHeight, maxHeightPx);

        _lastFramePixels = initialFrame.CopyRegionBytes(initialFrame.Bounds);
        _buffer = new byte[_stride * _viewportHeight];
        Buffer.BlockCopy(_lastFramePixels, 0, _buffer, 0, _lastFramePixels.Length);
        _currentHeight = _viewportHeight;
        FrameCount = 1;
    }

    public int Width => _width;

    public int ViewportHeight => _viewportHeight;

    public int CurrentHeight => _currentHeight;

    public int FrameCount { get; private set; }

    public StitchStepResult AppendFrame(ScreenFrame nextFrame)
    {
        ArgumentNullException.ThrowIfNull(nextFrame);
        if (nextFrame.Width != _width || nextFrame.Height != _viewportHeight)
            throw new ArgumentException("后续帧尺寸必须与初始选区严格一致。", nameof(nextFrame));

        if (_currentHeight >= _maxHeightPx)
        {
            return new StitchStepResult(StitchStatus.MaxHeightReached, 0, _currentHeight);
        }

        byte[] nextPixels = nextFrame.CopyRegionBytes(nextFrame.Bounds);

        int marginX = _width >= 48 ? Math.Clamp(_width / 14, 4, 36) : 0;
        int xStart = marginX;
        int xEnd = _width - marginX;
        int stepX = Math.Max(1, (xEnd - xStart) / 96);

        int topMargin = _viewportHeight >= 48 ? Math.Clamp(_viewportHeight / 10, 4, 88) : Math.Max(1, _viewportHeight / 16);
        int bottomMargin = _viewportHeight >= 48 ? Math.Clamp(_viewportHeight / 10, 4, 72) : Math.Max(1, _viewportHeight / 16);
        int bodyStart = topMargin;
        int bodyEnd = Math.Max(bodyStart + 8, _viewportHeight - bottomMargin);

        // 1. 快速静帧检测：中间主体区域平均通道差小于 1.2 视为画面未滚动
        if (ComputeMultiRowDiff(_lastFramePixels, nextPixels, bodyStart, bodyEnd, xStart, xEnd, stepX) < 1.2)
        {
            return new StitchStepResult(StitchStatus.NoMovement, 0, _currentHeight);
        }

        // 2. 在主体靠下区域选取具有足够纹理的参考模板带（跳过纯白空行，防止错误偏移匹配）
        int stripHeight = Math.Clamp(_viewportHeight / 10, 8, 36);
        if (!TrySelectBestTemplateRow(_lastFramePixels, bodyStart + stripHeight, bodyEnd - stripHeight, xStart, xEnd, stepX, stripHeight, out int refY))
        {
            return new StitchStepResult(StitchStatus.MatchFailed, 0, _currentHeight);
        }

        int minShift = Math.Max(4, _viewportHeight / 120);
        int maxShift = Math.Max(minShift + 1, refY - bodyStart);

        // 3. 由近及远粗略搜索最佳垂直位移 shiftY（向下滚动时，上一帧 refY 处的像素在新帧出现在 refY - shiftY）
        int bestShift = -1;
        double bestSad = double.MaxValue;
        for (int shift = minShift; shift <= maxShift; shift += 2)
        {
            double sad = ComputeStripSad(_lastFramePixels, refY, nextPixels, refY - shift, stripHeight, xStart, xEnd, stepX);
            if (sad < bestSad)
            {
                bestSad = sad;
                bestShift = shift;
            }
        }

        if (bestShift < 0 || bestSad > 28.0)
        {
            return new StitchStepResult(StitchStatus.MatchFailed, 0, _currentHeight);
        }

        // 4. ±2 px 精细亚行搜索（1px 步长）
        int fineLow = Math.Max(minShift, bestShift - 2);
        int fineHigh = Math.Min(maxShift, bestShift + 2);
        for (int shift = fineLow; shift <= fineHigh; shift++)
        {
            double sad = ComputeStripSad(_lastFramePixels, refY, nextPixels, refY - shift, stripHeight, xStart, xEnd, stepX);
            if (sad < bestSad)
            {
                bestSad = sad;
                bestShift = shift;
            }
        }

        // 5. 全重叠主体区域交叉校验，确保不是局部重复纹理误匹配
        int overlapRows = (bodyEnd - bodyStart) - bestShift;
        if (overlapRows >= 8)
        {
            double globalSad = ComputeWholeOverlapSad(
                _lastFramePixels, bodyStart + bestShift,
                nextPixels, bodyStart,
                overlapRows, xStart, xEnd, stepX);
            if (globalSad > 15.5)
            {
                return new StitchStepResult(StitchStatus.MatchFailed, 0, _currentHeight);
            }
        }
        else if (bestSad > 14.0)
        {
            return new StitchStepResult(StitchStatus.MatchFailed, 0, _currentHeight);
        }

        // 6. 检测固定底栏行数（非纯空白且连续两帧像素完全静止的底边栏），拼接时去重底栏并保留到底部
        int staticFooterRows = DetectStaticFooterRows(_lastFramePixels, nextPixels, bestShift, xStart, xEnd, stepX);

        int prevBodyHeight = Math.Max(0, _currentHeight - staticFooterRows);
        int sliceStartRow = _viewportHeight - staticFooterRows - bestShift;
        if (sliceStartRow < 0) sliceStartRow = 0;
        int newBodyRows = (_viewportHeight - staticFooterRows) - sliceStartRow;
        int actualAppendTotal = newBodyRows + staticFooterRows;
        int targetHeight = Math.Min(_maxHeightPx, prevBodyHeight + actualAppendTotal);

        EnsureCapacity(targetHeight);

        int maxWriteNewBody = Math.Min(newBodyRows, _maxHeightPx - prevBodyHeight);
        if (maxWriteNewBody > 0)
        {
            Buffer.BlockCopy(
                nextPixels, sliceStartRow * _stride,
                _buffer, prevBodyHeight * _stride,
                maxWriteNewBody * _stride);
        }

        int writtenBodyEnd = prevBodyHeight + maxWriteNewBody;
        int maxWriteFooter = Math.Min(staticFooterRows, _maxHeightPx - writtenBodyEnd);
        if (maxWriteFooter > 0)
        {
            Buffer.BlockCopy(
                nextPixels, (_viewportHeight - staticFooterRows) * _stride,
                _buffer, writtenBodyEnd * _stride,
                maxWriteFooter * _stride);
        }

        _currentHeight = writtenBodyEnd + maxWriteFooter;
        _lastFramePixels = nextPixels;
        FrameCount++;

        return new StitchStepResult(StitchStatus.Stitched, bestShift, _currentHeight);
    }

    public ScreenFrame BuildFrame()
    {
        int byteCount = _currentHeight * _stride;
        byte[] result = new byte[byteCount];
        Buffer.BlockCopy(_buffer, 0, result, 0, byteCount);
        return new ScreenFrame(new PxRect(0, 0, _width, _currentHeight), _stride, result);
    }

    /// <summary>生成轻量级全览缩略图（避免上万像素长图每帧完整拷贝造成 UI 卡顿）。</summary>
    public ScreenFrame BuildPreviewFrame(int maxWidth = 360, int maxHeight = 600)
    {
        if (_width <= maxWidth && _currentHeight <= maxHeight)
        {
            return BuildFrame();
        }

        double scale = Math.Min((double)maxWidth / _width, (double)maxHeight / _currentHeight);
        int outW = Math.Max(1, (int)Math.Round(_width * scale));
        int outH = Math.Max(1, (int)Math.Round(_currentHeight * scale));
        int outStride = outW * 4;
        byte[] dst = new byte[outH * outStride];

        for (int y = 0; y < outH; y++)
        {
            int sy = Math.Min(_currentHeight - 1, (int)(y / scale));
            int srcRow = sy * _stride;
            int dstRow = y * outStride;
            for (int x = 0; x < outW; x++)
            {
                int sx = Math.Min(_width - 1, (int)(x / scale));
                int si = srcRow + sx * 4;
                int di = dstRow + x * 4;
                dst[di] = _buffer[si];
                dst[di + 1] = _buffer[si + 1];
                dst[di + 2] = _buffer[si + 2];
                dst[di + 3] = 255;
            }
        }
        return new ScreenFrame(new PxRect(0, 0, outW, outH), outStride, dst);
    }

    private void EnsureCapacity(int requiredHeight)
    {
        int requiredBytes = requiredHeight * _stride;
        if (_buffer.Length >= requiredBytes) return;

        int nextRows = Math.Min(_maxHeightPx, Math.Max(requiredHeight, (_buffer.Length / _stride) * 2));
        var grown = new byte[nextRows * _stride];
        Buffer.BlockCopy(_buffer, 0, grown, 0, _currentHeight * _stride);
        _buffer = grown;
    }

    private double ComputeMultiRowDiff(
        byte[] a, byte[] b, int rowStart, int rowEnd, int xStart, int xEnd, int stepX)
    {
        const int sampleCount = 16;
        int rowSpan = Math.Max(1, rowEnd - rowStart);
        int rowStep = Math.Max(1, rowSpan / sampleCount);

        long totalDiff = 0;
        long samples = 0;
        for (int y = rowStart; y < rowEnd; y += rowStep)
        {
            int off = y * _stride;
            for (int x = xStart; x < xEnd; x += stepX)
            {
                int i = off + x * 4;
                totalDiff += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
                samples += 3;
            }
        }
        return samples == 0 ? 0 : (double)totalDiff / samples;
    }

    private bool TrySelectBestTemplateRow(
        byte[] pixels,
        int minY,
        int maxY,
        int xStart,
        int xEnd,
        int stepX,
        int stripHeight,
        out int chosenY)
    {
        chosenY = Math.Max(minY, maxY - stripHeight / 2);
        if (maxY < minY) return false;

        int bestY = chosenY;
        double bestGrad = -1;
        int candidates = 6;
        int span = Math.Max(1, maxY - minY);
        int stepY = Math.Max(1, span / candidates);

        // 从下往上挑选第一个梯度足够丰富的区域，兼顾大位移捕获率与抗白底能力
        for (int y = maxY; y >= minY; y -= stepY)
        {
            double grad = ComputeStripGradient(pixels, y, stripHeight, xStart, xEnd, stepX);
            if (grad >= 3.5)
            {
                chosenY = y;
                return true;
            }
            if (grad > bestGrad)
            {
                bestGrad = grad;
                bestY = y;
            }
        }

        chosenY = bestY;
        return bestGrad > 0.25;
    }

    private double ComputeStripGradient(byte[] pixels, int yStart, int height, int xStart, int xEnd, int stepX)
    {
        long total = 0;
        long samples = 0;
        int yEnd = Math.Min(_viewportHeight - 1, yStart + height);
        for (int y = yStart; y < yEnd; y += 2)
        {
            int rowOff = y * _stride;
            int nextRowOff = (y + 1) * _stride;
            for (int x = xStart; x < xEnd - stepX; x += stepX)
            {
                int i = rowOff + x * 4;
                int ir = rowOff + (x + stepX) * 4;
                int id = nextRowOff + x * 4;
                total += Math.Abs(pixels[i + 1] - pixels[ir + 1]) + Math.Abs(pixels[i + 1] - pixels[id + 1]);
                samples += 2;
            }
        }
        return samples == 0 ? 0 : (double)total / samples;
    }

    private double ComputeStripSad(
        byte[] prev, int prevY,
        byte[] next, int nextY,
        int stripHeight,
        int xStart, int xEnd, int stepX)
    {
        long total = 0;
        long samples = 0;
        for (int r = 0; r < stripHeight; r += 2)
        {
            int pOff = (prevY + r) * _stride;
            int nOff = (nextY + r) * _stride;
            for (int x = xStart; x < xEnd; x += stepX)
            {
                int pi = pOff + x * 4;
                int ni = nOff + x * 4;
                total += Math.Abs(prev[pi] - next[ni])
                       + Math.Abs(prev[pi + 1] - next[ni + 1])
                       + Math.Abs(prev[pi + 2] - next[ni + 2]);
                samples += 3;
            }
        }
        return samples == 0 ? double.MaxValue : (double)total / samples;
    }

    private double ComputeWholeOverlapSad(
        byte[] prev, int prevStartY,
        byte[] next, int nextStartY,
        int overlapRows,
        int xStart, int xEnd, int stepX)
    {
        int rowStep = Math.Max(1, overlapRows / 14);
        long total = 0;
        long samples = 0;
        for (int r = 0; r < overlapRows; r += rowStep)
        {
            int pOff = (prevStartY + r) * _stride;
            int nOff = (nextStartY + r) * _stride;
            for (int x = xStart; x < xEnd; x += stepX)
            {
                int pi = pOff + x * 4;
                int ni = nOff + x * 4;
                total += Math.Abs(prev[pi] - next[ni])
                       + Math.Abs(prev[pi + 1] - next[ni + 1])
                       + Math.Abs(prev[pi + 2] - next[ni + 2]);
                samples += 3;
            }
        }
        return samples == 0 ? 0 : (double)total / samples;
    }

    private int DetectStaticFooterRows(byte[] prev, byte[] next, int shiftY, int xStart, int xEnd, int stepX)
    {
        int maxFooter = Math.Min(_viewportHeight / 4, _viewportHeight - shiftY - 4);
        if (maxFooter <= 2) return 0;

        int staticRows = 0;
        bool hasTexture = false;
        for (int offset = 0; offset < maxFooter; offset++)
        {
            int y = _viewportHeight - 1 - offset;
            int off = y * _stride;
            long diffSum = 0;
            long rowGrad = 0;
            int count = 0;
            int prevG = next[off + xStart * 4 + 1];

            for (int x = xStart; x < xEnd; x += stepX)
            {
                int i = off + x * 4;
                diffSum += Math.Abs(prev[i] - next[i])
                         + Math.Abs(prev[i + 1] - next[i + 1])
                         + Math.Abs(prev[i + 2] - next[i + 2]);
                int g = next[i + 1];
                rowGrad += Math.Abs(g - prevG);
                prevG = g;
                count++;
            }

            double avgDiff = count > 0 ? (double)diffSum / (count * 3) : 0;
            if (avgDiff > 1.2) break;

            if (count > 0 && (double)rowGrad / count >= 1.0)
            {
                hasTexture = true;
            }
            staticRows++;
        }

        // 若底部纯属静止纯白边距而非真实底栏控件，则按正常滚动切片处理
        return hasTexture && staticRows >= 4 ? staticRows : 0;
    }
}
