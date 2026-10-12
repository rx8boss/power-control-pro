using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;

namespace PowerControlProWpf;

public partial class RepairResultWindow : Window
{
    internal RepairResultWindow(RepairResultSummary summary, ThemePalette palette, bool english = false)
    {
        InitializeComponent();
        var keys = new[] { "AppBrush", "CardBrush", "InputBrush", "TextBrush", "SubtleTextBrush",
            "AccentBrush", "CardBorderBrush", "ButtonBrush" };
        var colors = new[] { palette.Colors[0], palette.Colors[3], palette.Colors[4], palette.Colors[5],
            palette.Colors[6], palette.Colors[7], palette.IsNeon ? palette.Colors[7] : palette.Colors[8],
            palette.Colors[9] };
        for (var index = 0; index < keys.Length; index++)
            Resources[keys[index]] = Solid(colors[index]);
        Resources["ReadyBrush"] = Solid(palette.Ready);
        Resources["DangerBorderBrush"] = Solid(palette.Actions[1]);
        Resources["SuccessBrush"] = Solid(palette.Actions[2]);
        FontFamily = new FontFamily(palette.FontFamily);
        if (palette.Glow is not null)
        {
            ResultCard.Effect = new DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString(palette.Glow),
                ShadowDepth = 0, BlurRadius = 12, Opacity = palette.GlowOpacity * 0.55
            };
        }
        DialogIcon.Source = new BitmapImage(new Uri(
            $"pack://application:,,,/Assets/icons/{palette.IconSet}/status.png", UriKind.Absolute));
        DialogHeader.Text = english ? "Windows repair result" : "Windows-Reparatur: Ergebnis";
        ResultTitle.Text = summary.Title;
        ResultMessage.Text = summary.Message;
        ResultDetail.Text = summary.Detail;
        LaterButton.Content = summary.CanRestart
            ? (english ? "Later" : "Später") : (english ? "Close" : "Schließen");
        RestartButton.Content = english ? "Restart now" : "Jetzt neu starten";
        RestartButton.Visibility = summary.CanRestart ? Visibility.Visible : Visibility.Collapsed;
        if (!summary.IsSuccess)
        {
            ResultBadge.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "DangerBorderBrush");
            ResultTitle.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DangerBorderBrush");
            ResultSymbol.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DangerBorderBrush");
            ResultSymbol.Text = "!";
        }
    }

    private static Brush Solid(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private void DragDialog(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void CloseDialog(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void RestartDialog(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
}
