using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerControlProWpf;

namespace DialogSnapshot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
            throw new ArgumentException("Provide an absolute output directory.");

        Directory.CreateDirectory(args[0]);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var assembly = typeof(MainWindow).Assembly;
        var paletteType = assembly.GetType("PowerControlProWpf.ThemePalette", true)!;
        var palette = paletteType.GetMethod("For", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, ["Standard"])!;

        var messageType = assembly.GetType("PowerControlProWpf.ThemedMessageBoxWindow", true)!;
        Capture(messageType, ["Möchten Sie das Ereignisprotokoll unwiderruflich löschen?",
                "Ereignisprotokoll löschen?", MessageBoxButton.YesNo, MessageBoxImage.Warning, palette, false],
            Path.Combine(args[0], "delete.png"));
        Capture(messageType, ["Windows sperren jetzt ausführen?", "PowerLeitstand PRO",
                MessageBoxButton.YesNo, MessageBoxImage.Question, palette, false],
            Path.Combine(args[0], "lock.png"));
        foreach (var (name, question) in new[]
        {
            ("shutdown", "Herunterfahren jetzt ausführen?"),
            ("restart", "Neustart jetzt ausführen?"),
            ("signout", "Abmelden jetzt ausführen?")
        })
            Capture(messageType, [question, "PowerLeitstand PRO",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, palette, false],
                Path.Combine(args[0], name + ".png"));
        Capture(messageType, ["Laufende Windows-Reparatur abbrechen? Ein Abbruch kann den Reparaturvorgang unvollständig lassen.",
                "PowerLeitstand PRO", MessageBoxButton.YesNo, MessageBoxImage.Warning, palette, false],
            Path.Combine(args[0], "stop-repair.png"));

        var repairType = assembly.GetType("PowerControlProWpf.RepairConfirmWindow", true)!;
        Capture(repairType, ["DISM /Online /Cleanup-Image /RestoreHealth", palette, false],
            Path.Combine(args[0], "repair.png"));
        VerifyCopyCommand(repairType, palette);
        foreach (var theme in new[] { "Neon Blau", "Neon Pink", "Neon Grün", "Retro",
                     "Linux Mint", "Graphit", "Classic", "Dark" })
        {
            var themedPalette = paletteType.GetMethod("For", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [theme])!;
            Capture(messageType, ["Windows sperren jetzt ausführen?", "PowerLeitstand PRO",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, themedPalette, false],
                Path.Combine(args[0], theme + "-lock.png"));
            Capture(repairType, ["SFC /scannow", themedPalette, false],
                Path.Combine(args[0], theme + "-repair.png"));
        }
        Capture(messageType, ["Lock Windows now?", "PowerLeitstand PRO",
                MessageBoxButton.YesNo, MessageBoxImage.Question, palette, true],
            Path.Combine(args[0], "english-lock.png"));
        Capture(repairType, ["SFC /scannow", palette, true],
            Path.Combine(args[0], "english-repair.png"));
        app.Shutdown();
        return 0;
    }

    private static void VerifyCopyCommand(Type type, object palette)
    {
        const string command = "DISM /Online /Cleanup-Image /RestoreHealth";
        var constructor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(ctor => ctor.GetParameters().Length == 3);
        var window = (Window)constructor.Invoke([command, palette, false]);
        try
        {
            var copyButton = (Button)window.FindName("CopyButton")!;
            copyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (Clipboard.GetText() != command)
                throw new InvalidOperationException("Repair command was not copied correctly.");
        }
        finally { window.Close(); }
    }

    private static void Capture(Type type, object[] parameters, string file)
    {
        var constructor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(ctor => ctor.GetParameters().Length == parameters.Length);
        using var stream = File.Create(file);
        var window = (Window)constructor.Invoke(parameters);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowActivated = false;
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        window.Close();
        Console.WriteLine($"{Path.GetFileName(file)} {width}x{height}");
    }
}
