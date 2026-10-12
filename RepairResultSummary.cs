namespace PowerControlProWpf;

internal sealed record RepairResultSummary(
    string Title, string Message, string Detail, bool IsSuccess, bool CanRestart)
{
    internal static RepairResultSummary Create(string mode, int? exitCode, bool cancelled,
        string output, string hint, TimeSpan elapsed, bool english)
    {
        var tool = mode == "sfc" ? "SFC" : "DISM";
        var duration = elapsed.ToString(elapsed.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
        var detail = english ? $"{tool} · elapsed {duration}" : $"{tool} · Laufzeit {duration}";
        if (cancelled)
            return new(english ? "Repair cancelled" : "Reparatur abgebrochen",
                english ? "The repair did not finish. No result can be confirmed."
                    : "Die Reparatur wurde nicht beendet. Ein Ergebnis kann nicht bestätigt werden.",
                detail, false, false);

        if (exitCode != 0)
            return new(english ? "Repair reported an error" : "Fehler bei der Reparatur",
                string.IsNullOrWhiteSpace(hint)
                    ? (english ? "See the repair log for details." : "Details stehen im Reparaturprotokoll.")
                    : hint,
                detail + (english ? $" · exit code {exitCode?.ToString() ?? "unknown"}"
                    : $" · Exit-Code {exitCode?.ToString() ?? "unbekannt"}"), false, false);

        if (mode == "sfc")
        {
            if (ContainsAny(output, "nicht reparieren", "nicht repariert", "could not fix", "unable to fix", "could not repair"))
                return new(english ? "Files could not all be repaired" : "Nicht alle Dateien repariert",
                    english ? "Check CBS.log and run DISM before trying SFC again."
                        : "CBS.log prüfen und vor einem erneuten SFC-Lauf DISM ausführen.",
                    detail + " · Exit-Code 0", false, false);
            if (ContainsAny(output, "keine Integritätsverletzungen", "did not find any integrity violations"))
                return new(english ? "No integrity violations found" : "Keine Integritätsverletzungen gefunden",
                    english ? "SFC did not report damaged system files. A restart is optional."
                        : "SFC hat keine beschädigten Systemdateien gemeldet. Ein Neustart ist optional.",
                    detail + " · Exit-Code 0", true, true);
            if (ContainsAny(output, "erfolgreich repariert", "successfully repaired"))
                return new(english ? "System files repaired" : "Systemdateien repariert",
                    english ? "SFC reported that damaged files were repaired. You can restart now or later."
                        : "SFC hat beschädigte Dateien repariert. Du kannst jetzt oder später neu starten.",
                    detail + " · Exit-Code 0", true, true);
        }

        return new(english ? $"{tool} completed" : $"{tool} abgeschlossen",
            english ? "The command finished without a reported error. See the repair log for details."
                : "Der Befehl endete ohne gemeldeten Fehler. Details stehen im Reparaturprotokoll.",
            detail + " · Exit-Code 0", true, true);
    }

    private static bool ContainsAny(string text, params string[] phrases) =>
        phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}
