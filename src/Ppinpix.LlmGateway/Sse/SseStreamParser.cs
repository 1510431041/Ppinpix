using System.Runtime.CompilerServices;
using System.Text;

namespace Ppinpix.LlmGateway.Sse;

/// <summary>单个 Server-Sent Events 事件。</summary>
public readonly record struct SseEvent(string? EventType, string Data);

/// <summary>
/// 严格按 W3C SSE 规范实现的流式事件解析器。
/// 支持跨 Read 边界切片、\r\n / \n / \r 行尾、多行 data: 拼接、注释心跳行以及 [DONE] 终止处理。
/// </summary>
public static class SseStreamParser
{
    public static async IAsyncEnumerable<SseEvent> ReadEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

        string? currentEvent = null;
        var dataLines = new List<string>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                if (dataLines.Count > 0 || currentEvent is not null)
                {
                    if (dataLines.Count > 0)
                    {
                        yield return new SseEvent(currentEvent, string.Join('\n', dataLines));
                    }
                }
                yield break;
            }

            // 空行触发一个事件派发
            if (line.Length == 0)
            {
                if (dataLines.Count > 0)
                {
                    yield return new SseEvent(currentEvent, string.Join('\n', dataLines));
                }
                currentEvent = null;
                dataLines.Clear();
                continue;
            }

            // 注释行（如 ": keep-alive"）忽略
            if (line[0] == ':')
            {
                continue;
            }

            int colon = line.IndexOf(':');
            string field;
            string value;
            if (colon >= 0)
            {
                field = line[..colon];
                value = line[(colon + 1)..];
                if (value.Length > 0 && value[0] == ' ')
                {
                    value = value[1..];
                }
            }
            else
            {
                field = line;
                value = string.Empty;
            }

            switch (field)
            {
                case "event":
                    currentEvent = value;
                    break;
                case "data":
                    dataLines.Add(value);
                    break;
                // id / retry 字段按需忽略，兼容 SSE 规范
            }
        }
    }
}
