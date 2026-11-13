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

/// <summary>Anthropic Messages 协议适配器（/v1/messages，使用 x-api-key + anthropic-version 请求头）。</summary>
public sealed class AnthropicProvider : ILlmProvider
{
    private const string AnthropicVersion = "2023-06-01";
    private readonly HttpMessageHandler? _customHandler;

    public AnthropicProvider(HttpMessageHandler? customHandler = null)
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
        string url = HttpHelper.BuildEndpointUrl(profile.BaseUrl, profile.AutoAppendPath, "/messages");
        string payloadJson = BuildRequestJson(request, useStream);

        using var client = ProxyFactory.CreateClient(profile.Proxy, timeoutSec, _customHandler);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json"),
        };

        string apiKey = HttpHelper.DecryptApiKey(profile.EncryptedApiKey);
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpReq.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        }
        httpReq.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
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
                yield return new LlmStreamChunk(ParseNonStreamText(json), IsCompleted: true);
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

                    string evt = enumerator.Current.EventType ?? string.Empty;
                    string data = enumerator.Current.Data.Trim();
                    if (data.Length == 0) continue;

                    if (evt == "message_stop")
                    {
                        yield return new LlmStreamChunk(string.Empty, IsCompleted: true);
                        yield break;
                    }

                    if (TryExtractDelta(data, out string delta))
                    {
                        yield return new LlmStreamChunk(delta);
                    }
                }
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

        string url = profile.BaseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^"/messages".Length];
        }
        if (!url.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            url += "/models";
        }

        using var client = ProxyFactory.CreateClient(profile.Proxy, Math.Min(profile.TimeoutSeconds, 30), _customHandler);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        string apiKey = HttpHelper.DecryptApiKey(profile.EncryptedApiKey);
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        }
        req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        HttpHelper.ApplyExtraHeaders(req, profile.ExtraHeaders);

        try
        {
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            await HttpHelper.EnsureSuccessOrThrowAsync(resp, ct).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        string? id = idProp.GetString();
                        if (!string.IsNullOrWhiteSpace(id)) list.Add(id);
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

    private static string BuildRequestJson(LlmRequest request, bool stream)
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
            w.WriteString("model", request.Profile.Model.Trim());
            w.WriteNumber("max_tokens", request.Profile.MaxTokens is > 0 ? request.Profile.MaxTokens.Value : 4096);
            w.WriteBoolean("stream", stream);
            w.WriteNumber("temperature", Math.Clamp(request.Profile.Temperature, 0.0, 1.0));
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                w.WriteString("system", systemPrompt);
            }

            w.WriteStartArray("messages");
            foreach (var msg in request.Messages.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)))
            {
                w.WriteStartObject();
                w.WriteString("role", msg.Role);

                if (msg.PngImage is { Length: > 0 } imgBytes)
                {
                    var (b64, mediaType, _, _) = ImageEncoder.PrepareForUpload(imgBytes, msg.ImageWidth, msg.ImageHeight);
                    w.WriteStartArray("content");

                    w.WriteStartObject();
                    w.WriteString("type", "image");
                    w.WriteStartObject("source");
                    w.WriteString("type", "base64");
                    w.WriteString("media_type", mediaType);
                    w.WriteString("data", b64);
                    w.WriteEndObject();
                    w.WriteEndObject();

                    if (!string.IsNullOrEmpty(msg.Text))
                    {
                        w.WriteStartObject();
                        w.WriteString("type", "text");
                        w.WriteString("text", msg.Text);
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                }
                else
                {
                    w.WriteString("content", msg.Text);
                }

                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static bool TryExtractDelta(string json, out string text)
    {
        text = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var t) && t.GetString() == "error" &&
                root.TryGetProperty("error", out var err))
            {
                string msg = err.TryGetProperty("message", out var m) ? (m.GetString() ?? "Anthropic error") : err.ToString();
                throw new LlmException(LlmErrorKind.ProtocolError, msg);
            }

            if (root.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
            {
                if (delta.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                {
                    text = txt.GetString() ?? string.Empty;
                    return text.Length > 0;
                }
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ParseNonStreamText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                {
                    sb.Append(txt.GetString());
                }
            }
            return sb.ToString();
        }
        return string.Empty;
    }
}
