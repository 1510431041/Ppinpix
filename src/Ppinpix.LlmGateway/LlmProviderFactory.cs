using System.Net.Http;
using Ppinpix.Core.Settings;
using Ppinpix.LlmGateway.Providers;

namespace Ppinpix.LlmGateway;

/// <summary>根据 LlmProtocol 分发协议适配器。</summary>
public sealed class LlmProviderFactory
{
    private readonly HttpMessageHandler? _customHandler;

    public LlmProviderFactory() : this(null)
    {
    }

    public LlmProviderFactory(HttpMessageHandler? customHandler)
    {
        _customHandler = customHandler;
    }

    public ILlmProvider Create(LlmProtocol protocol) => protocol switch
    {
        LlmProtocol.AnthropicMessages => new AnthropicProvider(_customHandler),
        LlmProtocol.Gemini => new GeminiProvider(_customHandler),
        _ => new OpenAiProvider(_customHandler),
    };
}
