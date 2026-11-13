using Ppinpix.Core.Settings;

namespace Ppinpix.LlmGateway.Models;

/// <summary>大模型调用错误的语义分类，供 UI 友好提示及单测断言。</summary>
public enum LlmErrorKind
{
    None = 0,
    Cancelled,
    AuthFailed,
    RateLimited,
    EndpointNotFound,
    ModelNotFound,
    Timeout,
    ProxyError,
    NetworkError,
    TlsError,
    InvalidConfig,
    ProtocolError,
    ServerError,
}

public sealed record LlmMessage(string Role, string Text, byte[]? PngImage = null, int ImageWidth = 0, int ImageHeight = 0);

public sealed record LlmRequest(
    LlmProfile Profile,
    IReadOnlyList<LlmMessage> Messages,
    bool? OverrideStream = null,
    int? OverrideTimeoutSeconds = null);

public sealed record LlmStreamChunk(string DeltaText, bool IsCompleted = false, string? FinishReason = null);

public sealed record LlmResponse(
    string Text,
    int LatencyMs,
    string? ModelUsed = null,
    LlmErrorKind ErrorKind = LlmErrorKind.None,
    int? HttpStatusCode = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => ErrorKind == LlmErrorKind.None;
}

public sealed record ConnectionTestResult(
    bool Success,
    LlmErrorKind ErrorKind,
    int LatencyMs,
    string Message,
    int? HttpStatusCode = null,
    string? DiagnosticDetail = null);

public sealed class LlmException : Exception
{
    public LlmException(LlmErrorKind kind, string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        HttpStatusCode = statusCode;
    }

    public LlmErrorKind Kind { get; }

    public int? HttpStatusCode { get; }
}
