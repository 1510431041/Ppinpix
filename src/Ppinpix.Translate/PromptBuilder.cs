using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;
using Ppinpix.Ocr.Models;

namespace Ppinpix.Translate;

/// <summary>翻译 Prompt 构造器，支持内置默认模板与用户自定义 {source_lang} / {target_lang} / {text} 占位符模板。</summary>
public static class PromptBuilder
{
    public static IReadOnlyList<LlmMessage> BuildTextTranslation(
        LlmProfile profile,
        string text,
        string sourceLangPrompt,
        string targetLangPrompt)
    {
        if (!string.IsNullOrWhiteSpace(profile.Prompt))
        {
            string custom = profile.Prompt
                .Replace("{source_lang}", sourceLangPrompt, StringComparison.OrdinalIgnoreCase)
                .Replace("{target_lang}", targetLangPrompt, StringComparison.OrdinalIgnoreCase)
                .Replace("{text}", text, StringComparison.OrdinalIgnoreCase);

            if (!profile.Prompt.Contains("{text}", StringComparison.OrdinalIgnoreCase))
            {
                custom = custom.TrimEnd() + "\n\n" + text;
            }
            return [new LlmMessage("user", custom)];
        }

        string sys =
            $"You are a precise translator. Translate the user's text naturally and accurately into {targetLangPrompt}. " +
            "Preserve line breaks, code identifiers, numbers, URLs, and formatting. " +
            "Output ONLY the translation without explanations, quotes, or markdown wrappers.";

        return
        [
            new LlmMessage("system", sys),
            new LlmMessage("user", text),
        ];
    }

    public static IReadOnlyList<LlmMessage> BuildImageTranslation(
        LlmProfile profile,
        byte[] pngImage,
        int width,
        int height,
        string targetLangPrompt)
    {
        string target = string.Equals(targetLangPrompt, "auto", StringComparison.OrdinalIgnoreCase)
            ? "Simplified Chinese (or English if the image text is already Chinese)"
            : targetLangPrompt;

        string prompt = string.IsNullOrWhiteSpace(profile.Prompt)
            ? $"Extract all visible text in the image and translate it cleanly into {target}. Preserve paragraph layout. Output ONLY the translated text."
            : profile.Prompt
                .Replace("{source_lang}", "auto", StringComparison.OrdinalIgnoreCase)
                .Replace("{target_lang}", target, StringComparison.OrdinalIgnoreCase)
                .Replace("{text}", "[image]", StringComparison.OrdinalIgnoreCase);

        return [new LlmMessage("user", prompt, pngImage, width, height)];
    }

    public static IReadOnlyList<LlmMessage> BuildOverlayParagraphTranslation(
        IReadOnlyList<Paragraph> paragraphs,
        string targetLangPrompt,
        byte[]? pngImage = null,
        int imageWidth = 0,
        int imageHeight = 0)
    {
        string sys =
            $"Translate each indexed text item into {targetLangPrompt}. " +
            "Return strictly a JSON array of objects in the format [{\"id\": 0, \"text\": \"...\"}, ...]. " +
            "Do not add explanations or markdown fences.";

        var itemsJson = System.Text.Json.JsonSerializer.Serialize(
            paragraphs.Select((p, idx) => new { id = idx, text = p.Text }));

        var userMsg = pngImage is { Length: > 0 } && imageWidth > 0 && imageHeight > 0
            ? new LlmMessage("user", itemsJson, pngImage, imageWidth, imageHeight)
            : new LlmMessage("user", itemsJson);

        return
        [
            new LlmMessage("system", sys),
            userMsg,
        ];
    }
}
