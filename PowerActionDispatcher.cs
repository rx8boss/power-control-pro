namespace PowerControlProWpf;

internal enum PowerAction { Shutdown, Restart, SignOut, Lock }

internal static class PowerActionDispatcher
{
    public static void Execute(PowerAction action, Action<string, string> launch)
    {
        switch (action)
        {
            case PowerAction.Shutdown: launch("shutdown.exe", "/s /t 0"); break;
            case PowerAction.Restart: launch("shutdown.exe", "/r /t 0"); break;
            case PowerAction.SignOut: launch("shutdown.exe", "/l"); break;
            case PowerAction.Lock: launch("rundll32.exe", "user32.dll,LockWorkStation"); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }
}
