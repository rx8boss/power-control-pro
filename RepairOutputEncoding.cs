using System.Globalization;
using System.Text;

namespace PowerControlProWpf;

internal static class RepairOutputEncoding
{
    internal static Encoding ForMode(string mode)
    {
        if (mode == "sfc") return Encoding.Unicode;
        if (mode != "dism") throw new ArgumentOutOfRangeException(nameof(mode));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }
}
