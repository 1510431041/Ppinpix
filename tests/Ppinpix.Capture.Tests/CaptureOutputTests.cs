using System.IO;
using System.Text;
using Ppinpix.Capture.Output;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;
using Ppinpix.Core.Settings;
using Xunit;

namespace Ppinpix.Capture.Tests;

public sealed class CaptureOutputTests : IDisposable
{
    private readonly string _tempDir;

    public CaptureOutputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PpinpixCaptureTests_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void ExpandTemplate_AndBuildFilePath_ShouldFormatAndDeduplicate()
    {
        var dt = new DateTime(2026, 4, 5, 14, 9, 8);
        string name = CaptureOutput.ExpandTemplate("Shot_{yyyy}{MM}{dd}_{HH}{mm}{ss}_{w}x{h}", dt, new PxRect(0, 0, 320, 240));
        Assert.Equal("Shot_20260405_140908_320x240", name);

        var settings = new SaveSettings
        {
            Directory = _tempDir,
            Format = ImageFormat.Png,
            FileNameTemplate = "FixedName_{w}x{h}",
        };

        string p1 = CaptureOutput.BuildFilePath(settings, new PxRect(0, 0, 100, 50));
        File.WriteAllBytes(p1, []);
        string p2 = CaptureOutput.BuildFilePath(settings, new PxRect(0, 0, 100, 50));

        Assert.EndsWith("FixedName_100x50.png", p1);
        Assert.EndsWith("FixedName_100x50(2).png", p2);
    }

    [Fact]
    public void SaveToPath_PngJpegWebp_ShouldEncodeAndReloadWithValidPixels()
    {
        var frame = CreateSolidFrame(24, 16, b: 10, g: 120, r: 240, a: 255);

        string pngPath = Path.Combine(_tempDir, "test.png");
        string jpgPath = Path.Combine(_tempDir, "test.jpg");
        string webpPath = Path.Combine(_tempDir, "test.webp");

        CaptureOutput.SaveToPath(frame, pngPath, ImageFormat.Png);
        CaptureOutput.SaveToPath(frame, jpgPath, ImageFormat.Jpeg, 95);
        CaptureOutput.SaveToPath(frame, webpPath, ImageFormat.Webp, 92);

        byte[] webpBytes = File.ReadAllBytes(webpPath);
        Assert.True(webpBytes.Length > 12);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(webpBytes, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(webpBytes, 8, 4));

        var loadedPng = CaptureOutput.LoadFromFile(pngPath);
        var loadedJpg = CaptureOutput.LoadFromFile(jpgPath);
        var loadedWebp = CaptureOutput.LoadFromFile(webpPath);

        Assert.NotNull(loadedPng);
        Assert.NotNull(loadedJpg);
        Assert.NotNull(loadedWebp);

        Assert.Equal(24, loadedPng!.Width);
        Assert.Equal(16, loadedPng.Height);
        loadedPng.GetPixel(10, 8, out byte b, out byte g, out byte r, out byte a);
        Assert.Equal((10, 120, 240, 255), (b, g, r, a));

        Assert.Equal(24, loadedWebp!.Width);
        Assert.Equal(16, loadedWebp.Height);
    }

    private static ScreenFrame CreateSolidFrame(int w, int h, byte b, byte g, byte r, byte a)
    {
        int stride = w * 4;
        byte[] buf = new byte[stride * h];
        for (int i = 0; i < buf.Length; i += 4)
        {
            buf[i] = b;
            buf[i + 1] = g;
            buf[i + 2] = r;
            buf[i + 3] = a;
        }
        return new ScreenFrame(new PxRect(0, 0, w, h), stride, buf);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
