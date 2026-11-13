using System.IO;
using System.Text.Json;
using Ppinpix.Capture.Output;
using Ppinpix.Core;
using Ppinpix.Core.Capture;
using Ppinpix.Core.Geometry;

namespace Ppinpix.Pin;

public enum PinKind
{
    Image = 0,
    Text = 1,
}

/// <summary>单个贴图的序列化恢复元数据。</summary>
public sealed class SavedPinRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public PinKind Kind { get; set; } = PinKind.Image;
    public string? TextContent { get; set; }
    public string? Alias { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public double Zoom { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;
    public int RotationQuarterTurns { get; set; }
    public bool FlipH { get; set; }
    public bool FlipV { get; set; }
    public bool Topmost { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool Collapsed { get; set; }
    public bool Hidden { get; set; }
}

/// <summary>贴图会话持久化存储（%APPDATA%/Ppinpix/pins/pins.json + PNG）。</summary>
public static class PinStorage
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private static string ManifestPath => Path.Combine(Paths.PinsDirectory, "pins.json");

    public static void SaveAll(IReadOnlyList<(SavedPinRecord Record, ScreenFrame Frame)> items)
    {
        try
        {
            Directory.CreateDirectory(Paths.PinsDirectory);

            var keepIds = new HashSet<string>(items.Select(x => x.Record.Id), StringComparer.OrdinalIgnoreCase);

            // 清理不再存在的旧 PNG 文件
            foreach (string png in Directory.EnumerateFiles(Paths.PinsDirectory, "*.png"))
            {
                string id = Path.GetFileNameWithoutExtension(png);
                if (!keepIds.Contains(id))
                {
                    try { File.Delete(png); } catch { }
                }
            }

            var records = new List<SavedPinRecord>(items.Count);
            foreach (var (record, frame) in items)
            {
                string imgPath = Path.Combine(Paths.PinsDirectory, $"{record.Id}.png");
                byte[] bytes = CaptureOutput.EncodePng(frame);
                File.WriteAllBytes(imgPath, bytes);
                records.Add(record);
            }

            string tmp = ManifestPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(records, JsonOpts));
            if (File.Exists(ManifestPath)) File.Replace(tmp, ManifestPath, null, ignoreMetadataErrors: true);
            else File.Move(tmp, ManifestPath);
        }
        catch (Exception ex)
        {
            Log.Warn("保存贴图会话失败。", ex);
        }
    }

    public static List<(SavedPinRecord Record, ScreenFrame Frame)> LoadAll()
    {
        var result = new List<(SavedPinRecord Record, ScreenFrame Frame)>();
        try
        {
            if (!File.Exists(ManifestPath)) return result;
            string json = File.ReadAllText(ManifestPath);
            var records = JsonSerializer.Deserialize<List<SavedPinRecord>>(json, JsonOpts);
            if (records is null || records.Count == 0) return result;

            foreach (var rec in records)
            {
                if (string.IsNullOrWhiteSpace(rec.Id)) continue;
                string imgPath = Path.Combine(Paths.PinsDirectory, $"{rec.Id}.png");
                if (!File.Exists(imgPath)) continue;

                try
                {
                    var frame = CaptureOutput.LoadFromFile(imgPath);
                    if (frame is not null && frame.Width > 0 && frame.Height > 0)
                    {
                        var originFrame = new ScreenFrame(
                            new PxRect(rec.X, rec.Y, frame.Width, frame.Height),
                            frame.Stride,
                            frame.Pixels.ToArray());
                        result.Add((rec, originFrame));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"跳过损坏的贴图缓存 {imgPath}：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("读取保存的贴图列表失败。", ex);
        }
        return result;
    }

    public static void Clear()
    {
        try
        {
            if (Directory.Exists(Paths.PinsDirectory))
            {
                Directory.Delete(Paths.PinsDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("清理贴图缓存目录失败。", ex);
        }
    }
}
