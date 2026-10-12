using PowerControlProWpf;
using System.Text.Json;
using System.Text;

var sfcBytes = Encoding.Unicode.GetBytes("Überprüfung 34 % abgeschlossen.\r\n");
if (RepairOutputEncoding.ForMode("sfc").CodePage != Encoding.Unicode.CodePage ||
    RepairOutputEncoding.ForMode("dism").CodePage == Encoding.Unicode.CodePage)
    throw new Exception("Repair tool output encodings are not separated.");
using (var sfcReader = new StreamReader(new MemoryStream(sfcBytes), RepairOutputEncoding.ForMode("sfc")))
{
    var sfcLine = await sfcReader.ReadLineAsync();
    if (sfcLine != "Überprüfung 34 % abgeschlossen." ||
        !RepairProgressParser.TryReadPercent(sfcLine, out var sfcPercent) || sfcPercent != 34)
        throw new Exception("UTF-16 SFC progress was not decoded and parsed.");
}
if (!RepairProgressParser.TryReadPercent("3\04\0 \0%\0", out var legacyPercent) || legacyPercent != 34)
    throw new Exception("Legacy SFC progress with NUL characters was not parsed.");
Console.WriteLine("PASS SFC UTF-16 and legacy progress: 34 percent");

var cleanResult = RepairResultSummary.Create("sfc", 0, false,
    "Der Windows-Ressourcenschutz hat keine Integritätsverletzungen gefunden.", "", TimeSpan.FromMinutes(8), false);
var repairedResult = RepairResultSummary.Create("sfc", 0, false,
    "Beschädigte Dateien wurden erfolgreich repariert.", "", TimeSpan.FromMinutes(8), false);
var unresolvedResult = RepairResultSummary.Create("sfc", 0, false,
    "Einige Dateien konnten nicht repariert werden.", "", TimeSpan.FromMinutes(8), false);
var errorResult = RepairResultSummary.Create("dism", 87, false, "", "Ungültiger Parameter", TimeSpan.FromSeconds(10), false);
var cancelledResult = RepairResultSummary.Create("sfc", null, true, "", "", TimeSpan.FromSeconds(10), false);
if (!cleanResult.IsSuccess || !cleanResult.CanRestart ||
    !repairedResult.IsSuccess || !repairedResult.CanRestart ||
    unresolvedResult.IsSuccess || unresolvedResult.CanRestart ||
    errorResult.IsSuccess || errorResult.CanRestart ||
    cancelledResult.IsSuccess || cancelledResult.CanRestart)
    throw new Exception("Repair result classification failed.");
Console.WriteLine("PASS repair summary: clean, repaired, unresolved, failed and cancelled");

var finalMessage = JsonSerializer.Serialize(new RepairProtocol.Message("exit", "Exit-Code: 0", 0));
var readCount = 0;
var terminalReader = new FailingReader(finalMessage);
var terminalReceived = await RepairProtocol.ReadUntilTerminalAsync(terminalReader, message =>
{
    if (message.Kind != "exit" || message.Code != 0) throw new Exception("Wrong repair result.");
    readCount++;
});
if (!terminalReceived || readCount != 1 || terminalReader.ReadCount != 1)
    throw new Exception("Read continued after a terminal repair result.");
Console.WriteLine("PASS repair protocol: exit code 0 ends reading before broken pipe");

try
{
    await RepairProtocol.ReadUntilTerminalAsync(new FailingReader(JsonSerializer.Serialize(
        new RepairProtocol.Message("start", "started"))), _ => { });
    throw new Exception("Pipe failure without a terminal result was accepted.");
}
catch (IOException)
{
    Console.WriteLine("PASS repair protocol: broken pipe before result remains an error");
}

var repairEntry = new RepairLogEntry("11.10.2026 12:00:00", "DISM", "FEHLER", "0x800f081f source missing");
if (RepairLogEntry.Parse(repairEntry.ToLine()) != repairEntry ||
    RepairLogEntry.Parse("[old] plain message").Level != "INFO")
    throw new Exception("Repair log format or legacy parsing failed.");
Console.WriteLine("PASS repair log: structured entry and legacy text");

var estimator = new RepairTimeEstimator();
estimator.Add(TimeSpan.FromSeconds(1), 2);
estimator.Add(TimeSpan.FromSeconds(12), 8);
estimator.Add(TimeSpan.FromSeconds(25), 15);
if (estimator.Estimate(TimeSpan.FromSeconds(25)) is not TimeSpan eta || eta.TotalSeconds <= 0 ||
    estimator.Estimate(TimeSpan.FromSeconds(60)) is not null)
    throw new Exception("Repair ETA did not become available or expire correctly.");
estimator.Reset();
if (estimator.Estimate(TimeSpan.FromSeconds(30)) is not null)
    throw new Exception("Repair ETA was not reset.");
Console.WriteLine("PASS repair ETA: available, stale and reset");

foreach (var (line, expectedPercent) in new[]
{
    ("[===             5.5%             ]", 5.5),
    ("Überprüfung 42% abgeschlossen.", 42.0),
    ("[==================== 100,0% ]", 100.0)
})
{
    if (!RepairProgressParser.TryReadPercent(line, out var actual) || Math.Abs(actual - expectedPercent) > 0.001)
        throw new Exception($"Repair progress not parsed: {line}");
}
if (RepairProgressParser.TryReadPercent("Fehlercode 0x800f081f", out _))
    throw new Exception("Error code misread as repair progress.");
Console.WriteLine("PASS repair progress parser: DISM, SFC and error output");

var expected = new (PowerAction Action, string File, string Arguments)[]
{
    (PowerAction.Shutdown, "shutdown.exe", "/s /t 0"),
    (PowerAction.Restart, "shutdown.exe", "/r /t 0"),
    (PowerAction.SignOut, "shutdown.exe", "/l"),
    (PowerAction.Lock, "rundll32.exe", "user32.dll,LockWorkStation")
};

foreach (var (action, expectedFile, expectedArguments) in expected)
{
    var calls = new List<(string File, string Arguments)>();
    PowerActionDispatcher.Execute(action, (file, arguments) => calls.Add((file, arguments)));
    if (calls.Count != 1 || calls[0] != (expectedFile, expectedArguments))
        throw new Exception($"Wrong command mapping for {action}");
    Console.WriteLine($"PASS {action}: simulated {expectedFile} {expectedArguments}");
}

foreach (var (timerAction, expectedAction, german, english) in new[]
{
    ("shutdown", PowerAction.Shutdown, "Herunterfahren", "shutdown"),
    ("lock", PowerAction.Lock, "Windows sperren", "lock Windows"),
    ("restart", PowerAction.Restart, "Neustart", "restart")
})
{
    if (TimerActionDispatcher.Resolve(timerAction) != expectedAction ||
        TimerActionDispatcher.Label(timerAction, false) != german ||
        TimerActionDispatcher.Label(timerAction, true) != english)
        throw new Exception($"Incorrect timer action: {timerAction}");
    Console.WriteLine($"PASS timer {timerAction}: mapped and translated without executing Windows action");
}

try
{
    PowerActionDispatcher.Execute((PowerAction)999, (_, _) => { });
    throw new Exception("Invalid action was accepted");
}
catch (ArgumentOutOfRangeException)
{
    Console.WriteLine("PASS invalid action rejected");
}

var themes = new[]
{
    "Standard", "Neon Blau", "Neon Pink", "Neon Grün",
    "Retro", "Linux Mint", "Graphit", "Classic", "Dark"
};
foreach (var theme in themes)
{
    var palette = ThemePalette.For(theme);
    if (palette.Colors.Length != 11 || palette.Actions.Length != 6)
        throw new Exception($"Incomplete palette: {theme}");
    if (palette.IsNeon != theme.StartsWith("Neon ", StringComparison.Ordinal))
        throw new Exception($"Wrong neon state: {theme}");
    if (Contrast(palette.Colors[5], palette.Colors[3]) < 7 ||
        Contrast(palette.Colors[6], palette.Colors[3]) < 4.5 ||
        Contrast(palette.Colors[7], palette.Colors[9]) < 4.5 ||
        Contrast(palette.HeaderText, palette.Colors[1]) < 4.5 ||
        Contrast(palette.HeaderSubtle, palette.Colors[1]) < 4.5 ||
        Contrast(palette.FooterText, palette.Colors[2]) < 4.5)
        throw new Exception($"Palette text contrast too low: {theme}");
    foreach (var actionIndex in new[] { 0, 2, 3, 4 })
        if (Contrast("#F8FBFF", palette.Actions[actionIndex]) < 4.5)
            throw new Exception($"Action text contrast too low: {theme}, {actionIndex}");
    Console.WriteLine($"PASS {theme}: palette, glow and contrast");
}

static double Contrast(string first, string second)
{
    static double Luminance(string hex)
    {
        static double Channel(string value)
        {
            var component = Convert.ToInt32(value, 16) / 255.0;
            return component <= 0.04045 ? component / 12.92 : Math.Pow((component + 0.055) / 1.055, 2.4);
        }
        if (hex.Length != 7 || hex[0] != '#') throw new FormatException(hex);
        return 0.2126 * Channel(hex.Substring(1, 2)) +
               0.7152 * Channel(hex.Substring(3, 2)) +
               0.0722 * Channel(hex.Substring(5, 2));
    }
    var brighter = Math.Max(Luminance(first), Luminance(second));
    var darker = Math.Min(Luminance(first), Luminance(second));
    return (brighter + 0.05) / (darker + 0.05);
}

sealed class FailingReader(string firstLine) : StringReader(firstLine)
{
    internal int ReadCount { get; private set; }

    public override Task<string?> ReadLineAsync()
    {
        ReadCount++;
        if (ReadCount == 1) return Task.FromResult<string?>(base.ReadLine());
        throw new IOException("Pipe is broken.");
    }
}
