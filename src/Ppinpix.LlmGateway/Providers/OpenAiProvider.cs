using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;
using Ppinpix.LlmGateway.Processing;
using Ppinpix.LlmGateway.Proxy;
using Ppinpix.LlmGateway.Sse;

namespace Ppinpix.LlmGateway.Providers;

/// <summary>OpenAI Chat Completions 兼容协议适配器（适用于 OpenAI、DeepSeek、通义、Moonshot、Ollama 与各类聚合网关）。</summary>
public sealed class OpenAiProvider : ILlmProvider
{
    private readonly HttpMessageHandler? _customHandler;

    public OpenAiProvider(HttpMessageHandler? customHandler = null)
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
        string url = HttpHelper.BuildEndpointUrl(profile.BaseUrl, profile.AutoAppendPath, "/chat/completions");
        string payloadJson = BuildChatRequestJson(request, useStream);

        using var client = ProxyFactory.CreateClient(profile.Proxy, timeoutSec, _customHandler);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json"),
        };

        string apiKey = HttpHelper.DecryptApiKey(profile.EncryptedApiKey);
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
                string text = ParseNonStreamText(json);
                yield return new LlmStreamChunk(text, IsCompleted: true);
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
                    if (data.Length == 0) continue;
                    if (data == "[DONE]")
                    {
                        yield return new LlmStreamChunk(string.Empty, IsCompleted: true);
                        yield break;
                    }

                    if (TryExtractStreamDelta(data, out string delta, out string? finishReason))
                    {
                        bool done = finishReason is not null && finishReason != "null";
                        if (delta.Length > 0 || done)
                        {
                            yield return new LlmStreamChunk(delta, done, finishReason);
                        }
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

        string url = BuildModelsUrl(profile.BaseUrl);
        using var client = ProxyFactory.CreateClient(profile.Proxy, Math.Min(profile.TimeoutSeconds, 30), _customHandler);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        string apiKey = HttpHelper.DecryptApiKey(profile.EncryptedApiKey);
        if (!string.IsNullOrEmpty(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        HttpHelper.ApplyExtraHeaders(req, profile.ExtraHeaders);

        try
        {
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            await HttpHelper.EnsureSuccessOrThrowAsync(resp, ct).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseModelsList(json);
        }
        catch (Exception ex) when (ex is not LlmException)
        {
            throw HttpHelper.ClassifyException(ex, profile, ct);
        }
    }

    private static string BuildModelsUrl(string baseUrl)
    {
        string url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^"/chat/completions".Length];
        }
        if (url.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }
        return url + "/models";
    }

    private static string BuildChatRequestJson(LlmRequest request, bool stream)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("model", request.Profile.Model.Trim());
            w.WriteBoolean("stream", stream);
            w.WriteNumber("temperature", Math.Clamp(request.Profile.Temperature, 0.0, 2.0));
            if (request.Profile.MaxTokens is int maxTok && maxTok > 0)
            {
                w.WriteNumber("max_tokens", maxTok);
            }

            w.WriteStartArray("messages");
            foreach (var msg in request.Messages)
            {
                w.WriteStartObject();
                w.WriteString("role", msg.Role);

                if (msg.PngImage is { Length: > 0 } imgBytes)
                {
                    var (b64, mediaType, _, _) = ImageEncoder.PrepareForUpload(imgBytes, msg.ImageWidth, msg.ImageHeight);
                    w.WriteStartArray("content");

                    if (!string.IsNullOrEmpty(msg.Text))
                    {
                        w.WriteStartObject();
                        w.WriteString("type", "text");
                        w.WriteString("text", msg.Text);
                        w.WriteEndObject();
                    }

                    w.WriteStartObject();
                    w.WriteString("type", "image_url");
                    w.WriteStartObject("image_url");
                    w.WriteString("url", $"data:{mediaType};base64,{b64}");
                    w.WriteEndObject();
                    w.WriteEndObject();

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

    private static bool TryExtractStreamDelta(string json, out string deltaText, out string? finishReason)
    {
        deltaText = string.Empty;
        finishReason = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                string msg = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m)
                    ? (m.GetString() ?? "服务端返回错误事件")
                    : err.ToString();
                throw new LlmException(LlmErrorKind.ProtocolError, msg);
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return false;
            }

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
            {
                finishReason = fr.GetString();
            }

            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
            {
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    deltaText = content.GetString() ?? string.Empty;
                }
            }
            else if (choice.TryGetProperty("message", out var msgObj) &&
                     msgObj.TryGetProperty("content", out var msgContent) &&
                     msgContent.ValueKind == JsonValueKind.String)
            {
                deltaText = msgContent.GetString() ?? string.Empty;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ParseNonStreamText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException ex)
        {
            throw new LlmException(LlmErrorKind.ProtocolError, $"返回 JSON 格式异常：{ex.Message}", inner: ex);
        }
        return string.Empty;
    }

    private static IReadOnlyList<string> ParseModelsList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                {
                    string? id = idProp.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) models.Add(id);
                }
            }
        }
        return models.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
