using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Application = System.Windows.Application;
using FontFamily = System.Windows.Media.FontFamily;

namespace PowerControlProWpf;

internal static class ThemedMessageBox
{
    internal static MessageBoxResult Show(string message, string title = "Power Control PRO",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None) =>
        Show(Application.Current?.MainWindow, message, title, buttons, icon);

    internal static MessageBoxResult Show(Window? owner, string message, string title = "Power Control PRO",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var main = owner as MainWindow ?? Application.Current?.MainWindow as MainWindow;
        var dialog = new ThemedMessageBoxWindow(message, title, buttons, icon,
            ThemePalette.For(main?.CurrentThemeName ?? "Standard"), main?.IsEnglish ?? false);
        if (owner is { IsLoaded: true }) dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.Result;
    }
}

public partial class ThemedMessageBoxWindow : Window
{
    private readonly MessageBoxButton _buttons;
    internal MessageBoxResult Result { get; private set; }

    internal ThemedMessageBoxWindow(string message, string title, MessageBoxButton buttons,
        MessageBoxImage icon, ThemePalette palette, bool english)
    {
        InitializeComponent();
        _buttons = buttons;
        Result = buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.No;
        var lower = message.ToLowerInvariant();
        var deleting = lower.Contains("löschen") || lower.Contains("delete") || lower.Contains("clear");
        var stoppingRepair = lower.Contains("laufende windows-reparatur abbrechen") ||
                             lower.Contains("stop this running windows repair");
        var action = icon == MessageBoxImage.Question
            ? lower.Contains("windows sperren") || lower.Contains("lock windows") ? "lock"
            : lower.Contains("herunterfahren") || lower.Contains("shutdown") ? "shutdown"
            : lower.Contains("neustart") || lower.Contains("restart") ? "restart"
            : lower.Contains("abmelden") || lower.Contains("sign out") ? "signout"
            : null
            : null;
        var actionTitle = action switch
        {
            "lock" => english ? "Lock Windows?" : "Windows sperren?",
            "shutdown" => english ? "Shut down Windows?" : "Windows herunterfahren?",
            "restart" => english ? "Restart Windows?" : "Windows neu starten?",
            "signout" => english ? "Sign out?" : "Von Windows abmelden?",
            _ => title
        };
        Title = DialogTitle.Text = actionTitle;
        DialogMessage.Text = message;
        DialogSymbol.Text = icon switch
        {
            MessageBoxImage.Error => "×",
            MessageBoxImage.Warning => "!",
            MessageBoxImage.Question => "?",
            _ => "i"
        };

        if (icon == MessageBoxImage.Warning)
        {
            IconBadge.Visibility = Visibility.Collapsed;
            WarningTriangle.Visibility = Visibility.Visible;
            WarningMark.Visibility = Visibility.Visible;
        }
        else if (action is not null)
        {
            IconBadge.Visibility = Visibility.Collapsed;
            ActionGlyph.Data = IconGeometry(action);
            ActionGlyph.Fill = System.Windows.Media.Brushes.Transparent;
            ActionGlyph.Visibility = Visibility.Visible;
        }

        SetBrush("DialogBackground", palette.Colors[0]);
        Background = (Brush)Resources["DialogBackground"];
        SetBrush("DialogCard", palette.Colors[3]);
        Resources["DialogSurface"] = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(palette.Colors[3]),
            (Color)ColorConverter.ConvertFromString(palette.Colors[4]), 45);
        SetBrush("DialogText", palette.Colors[5]);
        SetBrush("DialogBorder", palette.Colors[7]);
        SetBrush("DialogButtonBackground", palette.Colors[9]);
        SetBrush("DialogHover", palette.Colors[10]);
        var surface = (Color)ColorConverter.ConvertFromString(palette.Colors[3]);
        var lightSurface = (surface.R * 299 + surface.G * 587 + surface.B * 114) / 1000 > 150;
        SetBrush("DialogDangerBackground", lightSurface ? "#98283F" : "#6A1F32");
        SetBrush("DialogDangerBorder", lightSurface ? "#A91F3D" : "#FF5A74");
        SetBrush("DialogDangerHover", lightSurface ? "#B93550" : "#98283F");
        SetBrush("DialogIconBrush", icon switch
        {
            MessageBoxImage.Error => palette.Actions[1],
            MessageBoxImage.Warning => palette.Actions[5],
            MessageBoxImage.Question => palette.Colors[7],
            _ => palette.Ready
        });
        FontFamily = new FontFamily(palette.FontFamily);

        if (buttons == MessageBoxButton.OK)
        {
            ConfirmButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = "OK";
            CancelButton.Style = (Style)FindResource("DialogButton");
            CancelButton.IsDefault = true;
            CancelButton.IsCancel = true;
            System.Windows.Controls.Grid.SetColumnSpan(CancelButton, 2);
            CancelButton.Margin = new Thickness(0);
        }
        else if (buttons == MessageBoxButton.YesNo)
        {
            var confirmCaption = deleting
                ? english ? "Delete" : "Löschen"
                : stoppingRepair ? english ? "Stop repair" : "Abbrechen"
                : action switch
                {
                    "lock" => english ? "Lock" : "Sperren",
                    "shutdown" => english ? "Shut down" : "Herunterfahren",
                    "restart" => english ? "Restart" : "Neustart",
                    "signout" => english ? "Sign out" : "Abmelden",
                    _ => english ? "Yes" : "Ja"
                };
            ConfirmButton.Content = deleting || stoppingRepair || action is not null
                ? CreateActionContent(confirmCaption, deleting ? "delete" : stoppingRepair ? "cancel" : action!,
                    deleting || stoppingRepair ? (Brush)Resources["DialogDangerBorder"] : (Brush)Resources["DialogIconBrush"])
                : confirmCaption;
            CancelButton.Content = stoppingRepair
                ? english ? "Continue" : "Fortsetzen"
                : CreateActionContent(english ? "Cancel" : "Abbrechen", "cancel",
                    (Brush)Resources["DialogDangerBorder"]);
            System.Windows.Automation.AutomationProperties.SetName(ConfirmButton, confirmCaption);
            System.Windows.Automation.AutomationProperties.SetName(CancelButton,
                stoppingRepair ? english ? "Continue" : "Fortsetzen" : english ? "Cancel" : "Abbrechen");
            if (deleting || stoppingRepair)
            {
                ConfirmButton.Style = (Style)FindResource("DangerButton");
                if (stoppingRepair) CancelButton.Style = (Style)FindResource("DialogButton");
            }
            CancelButton.IsDefault = true;
            CancelButton.IsCancel = true;
        }
        else
        {
            throw new NotSupportedException($"Unsupported dialog buttons: {buttons}");
        }
    }

    private static Geometry IconGeometry(string kind) => Geometry.Parse(kind switch
    {
        "lock" => "M 6,9 L 6,6 C 6,0 16,0 16,6 L 16,9 M 4,9 L 18,9 L 18,21 L 4,21 Z M 11,13 L 11,17",
        "shutdown" => "M 11,1 L 11,11 M 6,4 C 0,8 1,20 11,21 C 21,20 22,8 16,4",
        "restart" => "M 18,7 A 9,9 0 1 0 20,13 M 18,3 L 18,9 L 12,9",
        "signout" => "M 10,3 L 2,3 L 2,20 L 10,20 M 10,11 L 21,11 M 16,6 L 21,11 L 16,16",
        "delete" => "M 2,5 L 20,5 M 5,5 L 6,20 L 16,20 L 17,5 M 8,3 L 14,3 M 9,8 L 9,17 M 13,8 L 13,17",
        _ => "M 4,4 L 18,18 M 18,4 L 4,18"
    });

    private static StackPanel CreateActionContent(string caption, string kind, Brush iconBrush)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = IconGeometry(kind),
            Stroke = iconBrush,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 15,
            Height = 15,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 7, 0)
        };
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    private void SetBrush(string key, string hex) =>
        Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private void DragDialog(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void ConfirmDialog(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        DialogResult = true;
    }

    private void CancelDialog(object sender, RoutedEventArgs e)
    {
        Result = _buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.No;
        DialogResult = false;
    }
}
