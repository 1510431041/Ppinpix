using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;
using Ppinpix.LlmGateway.Processing;
using Ppinpix.LlmGateway.Proxy;
using Ppinpix.LlmGateway.Sse;

namespace Ppinpix.LlmGateway.Providers;

/// <summary>Google Gemini 原生 generateContent / streamGenerateContent 协议适配器（通过 x-goog-api-key 头鉴权，防止 Key 落入 URL 查询日志）。</summary>
public sealed class GeminiProvider : ILlmProvider
{
    private readonly HttpMessageHandler? _customHandler;

    public GeminiProvider(HttpMessageHandler? customHandler = null)
    {
        _customHandler = customHandler;
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var profile = request.Profile;
        HttpHelper.ValidateProfileForChat(profile);

        bool useStream = request.OverrideStream ?? profile.Stream;
        int timeoutSec = request.OverrideTimeoutSeconds ?? profile.TimeoutSeconds;

        string modelId = profile.Model.Trim();
        if (modelId.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            modelId = modelId["models/".Length..];
        }

        string url = profile.AutoAppendPath
            ? (useStream
                ? $"{profile.BaseUrl.Trim().TrimEnd('/')}/models/{Uri.EscapeDataString(modelId)}:streamGenerateContent?alt=sse"
                : $"{profile.BaseUrl.Trim().TrimEnd('/')}/models/{Uri.EscapeDataString(modelId)}:generateContent")
            : profile.BaseUrl.Trim();

        string payloadJson = BuildRequestJson(request);

        using var client = ProxyFactory.CreateClient(profile.Proxy, timeoutSec, _customHandler);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json"),
        };

        string apiKey = HttpHelper.DecryptApiKey(profile.EncryptedApiKey);
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpReq.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        }
        HttpHelper.ApplyExtraHeaders(httpReq, profile.ExtraHeaders);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not LlmException)
        {
            throw HttpHelper.ClassifyException(ex, profile, ct);
        }

        using (response)
        {
            await HttpHelper.EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            bool isSse = useStream && (mediaType is null || !mediaType.Contains("application/json", StringComparison.OrdinalIgnoreCase));

            if (!isSse)
            {
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                yield return new LlmStreamChunk(ExtractCandidateText(json), IsCompleted: true);
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var enumerator = SseStreamParser.ReadEventsAsync(stream, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not LlmException)
                    {
                        throw HttpHelper.ClassifyException(ex, profile, ct);
                    }
                    if (!moved) break;

                    string data = enumerator.Current.Data.Trim();
                    if (data.Length == 0 || data == "[DONE]") continue;

                    string delta = ExtractCandidateText(data);
                    if (delta.Length > 0)
                    {
                        yield return new LlmStreamChunk(delta);
                    }
                }
                yield return new LlmStreamChunk(string.Empty, IsCompleted: true);
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(LlmProfile profile, CancellationToken ct = default)
    {
        HttpHelper.ValidateProfileForModels(profile);
        string url = $"{profile.BaseUrl.Trim().TrimEnd('/')}/models";

        using var client = ProxyFactory.CreateClient(profile.Proxy, Math.Min(profile.TimeoutSeconds, 30), _customHandler);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        string apiKey = HttpHelper.DecryptApiKey(profile.EncryptedApiKey);
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        }
        HttpHelper.ApplyExtraHeaders(req, profile.ExtraHeaders);

        try
        {
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            await HttpHelper.EnsureSuccessOrThrowAsync(resp, ct).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in models.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    {
                        string? name = nameProp.GetString();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                        {
                            name = name["models/".Length..];
                        }
                        list.Add(name);
                    }
                }
            }
            return list.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is not LlmException)
        {
            throw HttpHelper.ClassifyException(ex, profile, ct);
        }
    }

    private static string BuildRequestJson(LlmRequest request)
    {
        string systemPrompt = string.Join(
            "\n\n",
            request.Messages.Where(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t)));

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                w.WriteStartObject("systemInstruction");
                w.WriteStartArray("parts");
                w.WriteStartObject();
                w.WriteString("text", systemPrompt);
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteStartArray("contents");
            foreach (var msg in request.Messages.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)))
            {
                w.WriteStartObject();
                w.WriteString("role", msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user");
                w.WriteStartArray("parts");

                if (!string.IsNullOrEmpty(msg.Text))
                {
                    w.WriteStartObject();
                    w.WriteString("text", msg.Text);
                    w.WriteEndObject();
                }

                if (msg.PngImage is { Length: > 0 } imgBytes)
                {
                    var (b64, mediaType, _, _) = ImageEncoder.PrepareForUpload(imgBytes, msg.ImageWidth, msg.ImageHeight);
                    w.WriteStartObject();
                    w.WriteStartObject("inlineData");
                    w.WriteString("mimeType", mediaType);
                    w.WriteString("data", b64);
                    w.WriteEndObject();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("generationConfig");
            w.WriteNumber("temperature", Math.Clamp(request.Profile.Temperature, 0.0, 2.0));
            if (request.Profile.MaxTokens is int maxTok && maxTok > 0)
            {
                w.WriteNumber("maxOutputTokens", maxTok);
            }
            w.WriteEndObject();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ExtractCandidateText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("candidates", out var cands) &&
                cands.ValueKind == JsonValueKind.Array && cands.GetArrayLength() > 0 &&
                cands[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var p in parts.EnumerateArray())
                {
                    if (p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(t.GetString());
                    }
                }
                return sb.ToString();
            }
        }
        catch (JsonException)
        {
        }
        return string.Empty;
    }
}
