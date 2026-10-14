using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerControlProWpf;

internal static partial class RepairProgressParser
{
    [GeneratedRegex(@"(?<!\d)(\d{1,3}(?:[.,]\d+)?)\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentPattern();

    internal static bool TryReadPercent(string line, out double percent)
    {
        percent = 0;
        // Also recognize entries saved by older builds that decoded SFC UTF-16 as OEM.
        var match = PercentPattern().Match(line.Replace("\0", ""));
        return match.Success
            && double.TryParse(match.Groups[1].Value.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out percent)
            && percent is >= 0 and <= 100;
    }
}
