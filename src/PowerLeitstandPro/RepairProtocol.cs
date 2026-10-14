using System.IO;
using System.Text.Json;

namespace PowerControlProWpf;

internal static class RepairProtocol
{
    internal sealed record Message(string Kind, string Text, int Code = 0);

    // A terminal message completes the protocol. Do not read the pipe again:
    // Windows can report ERROR_BROKEN_PIPE instead of a clean EOF after the helper closes it.
    internal static async Task<bool> ReadUntilTerminalAsync(TextReader reader, Action<Message> onMessage)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            Message? message;
            try { message = JsonSerializer.Deserialize<Message>(line); }
            catch (JsonException) { continue; }
            if (message is null) continue;
            onMessage(message);
            if (message.Kind is "exit" or "cancelled") return true;
        }
        return false;
    }
}
