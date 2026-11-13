using Ppinpix.Ocr.Models;

namespace Ppinpix.Ocr.Processing;

/// <summary>
/// 按 Y 轴坐标、行间距与水平投影重叠关系把零散文本行聚类为段落。
/// 多栏排版（水平存在明显列间距且水平投影不重叠）不会错误跨栏粘连。
/// </summary>
public static class ParagraphGrouper
{
    public static IReadOnlyList<Paragraph> Group(IReadOnlyList<TextLine> lines)
    {
        if (lines.Count == 0) return [];

        // 按顶部坐标主排序，相同高度 band 内按左边界排序
        var sorted = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .OrderBy(l => l.Box.MinY)
            .ThenBy(l => l.Box.MinX)
            .ToList();

        if (sorted.Count == 0) return [];

        var buckets = new List<List<TextLine>>();

        foreach (var line in sorted)
        {
            List<TextLine>? bestBucket = null;
            double bestScore = double.MaxValue;

            foreach (var bucket in buckets)
            {
                var last = bucket[^1];
                double refH = Math.Max(8.0, Math.Max(last.Box.Height, line.Box.Height));
                double vGap = line.Box.MinY - last.Box.MaxY;

                // 同一段允许轻微上下重叠（弯曲文本行）到最多 0.85 倍行高行距
                if (vGap > refH * 0.85 || vGap < -refH * 0.5)
                {
                    continue;
                }

                // 若两行高度几乎同行（并排两栏），绝不能合进同一个桶
                if (Math.Abs(line.Box.CenterY - last.Box.CenterY) < Math.Min(last.Box.Height, line.Box.Height) * 0.45 &&
                    HorizontalGap(last.Box, line.Box) > refH * 1.2)
                {
                    continue;
                }

                // 水平投影必须存在足够接近或重叠（禁止跨越双栏大空白合并）
                double hGap = HorizontalGap(BucketBox(bucket), line.Box);
                if (hGap > refH * 1.8)
                {
                    continue;
                }

                // 避免首行极左缩进差过大（右栏标题与左栏正文错位时）
                double indentDiff = Math.Abs(line.Box.MinX - bucket[0].Box.MinX);
                if (OverlapX(BucketBox(bucket), line.Box) <= 0 && indentDiff > refH * 2.5)
                {
                    continue;
                }

                double score = Math.Abs(vGap) + hGap * 0.5;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestBucket = bucket;
                }
            }

            if (bestBucket is not null)
            {
                bestBucket.Add(line);
            }
            else
            {
                buckets.Add([line]);
            }
        }

        // 先按桶的顶部粗网格分区域排序：若处于同一大行分栏则先左栏后右栏，整体从上到下
        var result = buckets
            .Select(BuildParagraph)
            .OrderBy(p => Math.Round(p.Box.MinY / 20.0))
            .ThenBy(p => p.Box.MinX)
            .ToList();

        return result;
    }

    private static Paragraph BuildParagraph(List<TextLine> lines)
    {
        var box = BucketBox(lines);
        string text = string.Join('\n', lines.Select(l => l.Text));
        return new Paragraph(text, box, lines);
    }

    private static Quad BucketBox(IReadOnlyList<TextLine> lines)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var l in lines)
        {
            if (l.Box.MinX < minX) minX = l.Box.MinX;
            if (l.Box.MinY < minY) minY = l.Box.MinY;
            if (l.Box.MaxX > maxX) maxX = l.Box.MaxX;
            if (l.Box.MaxY > maxY) maxY = l.Box.MaxY;
        }
        return Quad.FromRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static double OverlapX(Quad a, Quad b) =>
        Math.Max(0, Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX));

    private static double HorizontalGap(Quad a, Quad b)
    {
        if (a.MaxX < b.MinX) return b.MinX - a.MaxX;
        if (b.MaxX < a.MinX) return a.MinX - b.MaxX;
        return 0;
    }
}
