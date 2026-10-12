namespace PowerControlProWpf;

internal sealed record RepairLogEntry(string Timestamp, string Phase, string Level, string Message)
{
    internal string ToLine() => $"[{Timestamp}] [{Phase}] [{Level}] {Message}";

    internal static RepairLogEntry Parse(string line)
    {
        var parts = line.Split("] [", 3, StringSplitOptions.None);
        if (parts.Length == 3 && parts[0].StartsWith('[') && parts[2].Contains("] "))
        {
            var last = parts[2].Split("] ", 2, StringSplitOptions.None);
            return new(parts[0][1..], parts[1], last[0], last[1]);
        }
        return new("", "ALT", "INFO", line);
    }
}
