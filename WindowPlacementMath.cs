using System.Drawing;

namespace PowerControlProWpf;

internal static class WindowPlacementMath
{
    internal static Point Center(Rectangle workArea, int width, int height) => new(
        workArea.Left + (workArea.Width - width) / 2,
        workArea.Top + (workArea.Height - height) / 2);

    internal static Point RestoreOrCenter(
        Rectangle preferredWorkArea, IReadOnlyList<(string Name, Rectangle WorkArea)> screens,
        string? savedMonitor, int? savedLeft, int? savedTop, int width, int height)
    {
        if (!string.IsNullOrWhiteSpace(savedMonitor) && savedLeft is int left && savedTop is int top)
        {
            foreach (var screen in screens)
            {
                if (!string.Equals(screen.Name, savedMonitor, StringComparison.OrdinalIgnoreCase)) continue;
                if (width > screen.WorkArea.Width || height > screen.WorkArea.Height) break;
                var saved = new Rectangle(left, top, width, height);
                var visible = Rectangle.Intersect(saved, screen.WorkArea);
                if (visible.Width < 64 || visible.Height < 64) break;
                return new Point(
                    Math.Clamp(left, screen.WorkArea.Left, screen.WorkArea.Right - width),
                    Math.Clamp(top, screen.WorkArea.Top, screen.WorkArea.Bottom - height));
            }
        }
        return Center(preferredWorkArea, width, height);
    }
}
