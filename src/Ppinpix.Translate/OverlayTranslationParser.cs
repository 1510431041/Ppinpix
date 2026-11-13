using System.Text.Json;
using Ppinpix.Ocr.Models;

namespace Ppinpix.Translate;

/// <summary>
/// 增量解析大模型流式返回的 JSON 数组 `[{\"id\":0,\"text\":\"...\"}]`，
/// 让原位覆盖翻译能在生成第一个段落时立即局部渲染，并在格式偏离时优雅回退。
/// </summary>
public static class OverlayTranslationParser
{
    public static Dictionary<int, string> ParseIncremental(string streamedOutput, int expectedParagraphCount)
    {
        var map = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(streamedOutput) || expectedParagraphCount <= 0)
        {
            return map;
        }

        string cleaned = StripCodeFence(streamedOutput);

        // 逐个扫描闭合 JSON 对象 `{ ... }`，即使外层 `[` 尚未收到结尾 `]` 也能即时产出已完成段
        int i = 0;
        while (i < cleaned.Length)
        {
            int objStart = cleaned.IndexOf('{', i);
            if (objStart < 0) break;

            int objEnd = FindMatchingBrace(cleaned, objStart);
            if (objEnd < 0) break;

            string objJson = cleaned[objStart..(objEnd + 1)];
            TryParseObject(objJson, expectedParagraphCount, map);
            i = objEnd + 1;
        }

        // 若完全不是 JSON（某些小模型直接输出纯译文），当流较完整或包含多行时按段切分回退
        if (map.Count == 0 && !cleaned.Contains('{'))
        {
            string[] split = cleaned.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);
            if (split.Length == 1 && expectedParagraphCount > 1)
            {
                split = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            for (int k = 0; k < Math.Min(expectedParagraphCount, split.Length); k++)
            {
                map[k] = split[k].Trim();
            }
        }

        return map;
    }

    private static void TryParseObject(string json, int expectedCount, Dictionary<int, string> map)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            int id = -1;
            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out int parsed))
            {
                id = parsed;
            }
            if (id < 0 || id >= expectedCount) return;

            string? text = null;
            if (root.TryGetProperty("text", out var txtProp) && txtProp.ValueKind == JsonValueKind.String)
            {
                text = txtProp.GetString();
            }
            else if (root.TryGetProperty("translation", out var trProp) && trProp.ValueKind == JsonValueKind.String)
            {
                text = trProp.GetString();
            }

            if (text is not null)
            {
                map[id] = text;
            }
        }
        catch (JsonException)
        {
        }
    }

    private static int FindMatchingBrace(string s, int startIndex)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = startIndex; i < s.Length; i++)
        {
            char c = s[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (inString)
            {
                if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static string StripCodeFence(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("```"))
        {
            int firstNl = t.IndexOf('\n');
            if (firstNl >= 0) t = t[(firstNl + 1)..];
            int lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence > 0) t = t[..lastFence];
        }
        return t.Trim();
    }
}
