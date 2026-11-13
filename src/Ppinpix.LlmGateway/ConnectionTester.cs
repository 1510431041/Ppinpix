using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;

namespace Ppinpix.LlmGateway;

/// <summary>真实发送一次极简流式探测请求，验证 URL、代理、API Key 与模型可用性并返回耗时与分类错误。</summary>
public sealed class ConnectionTester
{
    private readonly LlmProviderFactory _factory;

    public ConnectionTester() : this(null)
    {
    }

    public ConnectionTester(HttpMessageHandler? customHandler)
    {
        _factory = new LlmProviderFactory(customHandler);
    }

    public async Task<ConnectionTestResult> TestAsync(LlmProfile profile, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var provider = _factory.Create(profile.Protocol);
            var req = new LlmRequest(
                profile,
                [new LlmMessage("user", "Reply with OK only.")],
                OverrideStream: profile.Stream,
                OverrideTimeoutSeconds: Math.Min(profile.TimeoutSeconds, 20));

            var sb = new StringBuilder();
            await foreach (var chunk in provider.StreamCompleteAsync(req, ct).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(chunk.DeltaText))
                {
                    sb.Append(chunk.DeltaText);
                }
            }

            sw.Stop();
            int ms = (int)sw.ElapsedMilliseconds;
            string replyPreview = sb.ToString().Trim();
            if (replyPreview.Length > 40) replyPreview = replyPreview[..40] + "…";
            string msg = string.IsNullOrEmpty(replyPreview)
                ? $"连接成功（响应为空，耗时 {ms} ms）"
                : $"连接成功 · 延迟 {ms} ms · 返回 \"{replyPreview}\"";

            return new ConnectionTestResult(true, LlmErrorKind.None, ms, msg);
        }
        catch (LlmException ex)
        {
            sw.Stop();
            return new ConnectionTestResult(
                false,
                ex.Kind,
                (int)sw.ElapsedMilliseconds,
                ex.Message,
                ex.HttpStatusCode,
                ex.InnerException?.Message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(
                false,
                LlmErrorKind.NetworkError,
                (int)sw.ElapsedMilliseconds,
                ex.Message);
        }
    }
}
