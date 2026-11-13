using Ppinpix.Ocr.Models;
using Ppinpix.Ocr.Processing;
using Xunit;

namespace Ppinpix.Ocr.Tests;

public sealed class ParagraphGrouperTests
{
    private static TextLine MakeLine(string text, double x, double y, double w, double h) =>
        new(text, Quad.FromRect(x, y, w, h), []);

    [Fact]
    public void Group_SingleColumnLines_ShouldMergeIntoOneParagraph()
    {
        var lines = new[]
        {
            MakeLine("First line of text", 20, 20, 200, 18),
            MakeLine("Second line of text", 22, 44, 195, 18),
            MakeLine("Third line of text", 20, 68, 180, 18),
        };

        var paragraphs = ParagraphGrouper.Group(lines);
        Assert.Single(paragraphs);
        Assert.Equal("First line of text\nSecond line of text\nThird line of text", paragraphs[0].Text);
    }

    [Fact]
    public void Group_TwoColumnsSideBySide_ShouldRemainIsolated()
    {
        var lines = new[]
        {
            MakeLine("Left column top", 20, 20, 140, 18),
            MakeLine("Right column top", 300, 22, 140, 18),
            MakeLine("Left column second", 20, 44, 140, 18),
            MakeLine("Right column second", 300, 46, 140, 18),
        };

        var paragraphs = ParagraphGrouper.Group(lines);
        Assert.Equal(2, paragraphs.Count);
        Assert.Contains("Left column top", paragraphs[0].Text);
        Assert.Contains("Left column second", paragraphs[0].Text);
        Assert.Contains("Right column top", paragraphs[1].Text);
        Assert.Contains("Right column second", paragraphs[1].Text);
    }
}
