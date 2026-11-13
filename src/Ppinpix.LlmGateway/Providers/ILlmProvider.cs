using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Models;

namespace Ppinpix.LlmGateway.Providers;

/// <summary>大模型协议适配器接口（OpenAI 兼容 / Anthropic Messages / Gemini 原生）。</summary>
public interface ILlmProvider
{
    IAsyncEnumerable<LlmStreamChunk> StreamCompleteAsync(LlmRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListModelsAsync(LlmProfile profile, CancellationToken ct = default);
}
