using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text.Json;
using Ppinpix.Core;
using Ppinpix.Core.Security;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;

namespace Ppinpix.LlmGateway.Providers;

internal static class HttpHelper
{
    private static readonly HashSet<string> DisallowedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Transfer-Encoding",
        "Connection",
        "Upgrade",
        "Keep-Alive",
        "Proxy-Connection",
    };

    public static string BuildEndpointUrl(string baseUrl, bool autoAppendPath, string suffixPath)
    {
        string trimmed = (baseUrl ?? string.Empty).Trim();
        if (!autoAppendPath)
        {
            return trimmed;
        }

        trimmed = trimmed.TrimEnd('/');
        string normalizedSuffix = suffixPath.StartsWith('/') ? suffixPath : "/" + suffixPath;
        if (trimmed.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        // OpenAI /v1/models 时，若 suffix 已有特定路径（如 /models）则避免重复 /v1/v1
        return trimmed + normalizedSuffix;
    }

    public static string DecryptApiKey(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return string.Empty;
        try
        {
            return SecretStore.Unprotect(encrypted);
        }
        catch (Exception ex)
        {
            Log.Warn($"解密 API Key 失败：{Log.Redact(ex.Message)}");
            return string.Empty;
        }
    }

    public static void ApplyExtraHeaders(HttpRequestMessage req, IReadOnlyDictionary<string, string>? extraHeaders)
    {
        req.Headers.UserAgent.Clear();
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Ppinpix", "1.0"));

        if (extraHeaders is null) return;
        foreach (var (k, v) in extraHeaders)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            string key = k.Trim();
            if (DisallowedHeaders.Contains(key)) continue;
            req.Headers.TryAddWithoutValidation(key, v ?? string.Empty);
        }
    }

    public static void ValidateProfileForChat(LlmProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.BaseUrl) ||
            !Uri.TryCreate(profile.BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new LlmException(LlmErrorKind.InvalidConfig, "接口地址（Base URL）必须是有效的 http 或 https 链接。");
        }

        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            throw new LlmException(LlmErrorKind.InvalidConfig, "模型名称不能为空，请先填写或拉取模型列表后选择模型。");
        }
    }

    public static void ValidateProfileForModels(LlmProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.BaseUrl) ||
            !Uri.TryCreate(profile.BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new LlmException(LlmErrorKind.InvalidConfig, "接口地址（Base URL）必须是有效的 http 或 https 链接。");
        }
    }

    public static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        int code = (int)response.StatusCode;
        string body = string.Empty;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
        }

        string safeMsg = ExtractApiErrorMessage(body);
        if (safeMsg.Length > 260) safeMsg = safeMsg[..260] + "…";
        safeMsg = Log.Redact(safeMsg);

        LlmErrorKind kind = ClassifyStatusCode(response.StatusCode, safeMsg);
        string hint = kind switch
        {
            LlmErrorKind.AuthFailed => "鉴权失败（API Key 无效、过期或权限不足）",
            LlmErrorKind.EndpointNotFound => "接口路径不存在（404），请检查 Base URL 与自动补全开关",
            LlmErrorKind.ModelNotFound => "模型不存在或无权访问，请检查模型名",
            LlmErrorKind.RateLimited => "请求过于频繁或额度超限（429）",
            LlmErrorKind.ServerError => $"服务端异常（HTTP {code}）",
            _ => $"请求被拒绝（HTTP {code}）",
        };

        string fullMsg = string.IsNullOrWhiteSpace(safeMsg) ? hint : $"{hint}：{safeMsg}";
        throw new LlmException(kind, fullMsg, code);
    }

    public static LlmException ClassifyException(Exception ex, LlmProfile profile, CancellationToken userCt)
    {
        if (ex is LlmException llmEx) return llmEx;

        if (ex is OperationCanceledException)
        {
            return userCt.IsCancellationRequested
                ? new LlmException(LlmErrorKind.Cancelled, "请求已取消。", inner: ex)
                : new LlmException(LlmErrorKind.Timeout, $"请求超时（超过 {profile.TimeoutSeconds} 秒未响应）。", inner: ex);
        }

        if (ex is InvalidOperationException && profile.Proxy.Mode is ProxyMode.Http or ProxyMode.Socks5)
        {
            return new LlmException(LlmErrorKind.ProxyError, $"代理配置错误：{Log.Redact(ex.Message)}", inner: ex);
        }

        if (ex is HttpRequestException hre)
        {
            if (HasInner<AuthenticationException>(hre) ||
                hre.HttpRequestError == HttpRequestError.SecureConnectionError)
            {
                return new LlmException(LlmErrorKind.TlsError, $"TLS / SSL 安全连接握手失败：{Log.Redact(hre.Message)}", inner: ex);
            }

            if (hre.HttpRequestError == HttpRequestError.ProxyTunnelError ||
                profile.Proxy.Mode is ProxyMode.Http or ProxyMode.Socks5 &&
                hre.Message.Contains("proxy", StringComparison.OrdinalIgnoreCase))
            {
                return new LlmException(LlmErrorKind.ProxyError, $"代理连接失败：{Log.Redact(hre.Message)}", inner: ex);
            }

            return new LlmException(LlmErrorKind.NetworkError, $"网络连接失败：{Log.Redact(hre.Message)}", inner: ex);
        }

        return new LlmException(LlmErrorKind.NetworkError, $"请求发生异常：{Log.Redact(ex.Message)}", inner: ex);
    }

    private static LlmErrorKind ClassifyStatusCode(HttpStatusCode status, string errorText)
    {
        int code = (int)status;
        if (code is 401 or 403) return LlmErrorKind.AuthFailed;
        if (code == 429) return LlmErrorKind.RateLimited;
        if (code >= 500) return LlmErrorKind.ServerError;
        if (code == 404)
        {
            return errorText.Contains("model", StringComparison.OrdinalIgnoreCase)
                ? LlmErrorKind.ModelNotFound
                : LlmErrorKind.EndpointNotFound;
        }
        if (errorText.Contains("model", StringComparison.OrdinalIgnoreCase) &&
            (errorText.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             errorText.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
             errorText.Contains("unsupported", StringComparison.OrdinalIgnoreCase)))
        {
            return LlmErrorKind.ModelNotFound;
        }
        return LlmErrorKind.ProtocolError;
    }

    private static string ExtractApiErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? body;
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString() ?? body;
            }
            if (root.TryGetProperty("message", out var topMsg))
            {
                return topMsg.GetString() ?? body;
            }
        }
        catch
        {
        }
        return body.Trim();
    }

    private static bool HasInner<T>(Exception? ex) where T : Exception
    {
        while (ex is not null)
        {
            if (ex is T) return true;
            ex = ex.InnerException;
        }
        return false;
    }
}
