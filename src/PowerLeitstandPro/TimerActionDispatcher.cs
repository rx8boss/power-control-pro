namespace PowerControlProWpf;

internal static class TimerActionDispatcher
{
    internal static PowerAction Resolve(string action) => action switch
    {
        "shutdown" => PowerAction.Shutdown,
        "restart" => PowerAction.Restart,
        "lock" => PowerAction.Lock,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    internal static string Label(string action, bool english) => action switch
    {
        "shutdown" => english ? "shutdown" : "Herunterfahren",
        "restart" => english ? "restart" : "Neustart",
        "lock" => english ? "lock Windows" : "Windows sperren",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
