using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PowerControlProWpf;

namespace LayoutSnapshot;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Provide an absolute output directory.");
        Directory.CreateDirectory(args[0]);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var sound in new[] { "warnings/beep.wav", "clicks/soft.wav" })
        {
            var assemblyName = typeof(MainWindow).Assembly.GetName().Name;
            var resource = Application.GetResourceStream(new Uri($"/{assemblyName};component/Assets/sounds/{sound}", UriKind.Relative));
            if (resource is null) throw new InvalidOperationException($"UI sound is missing: {sound}.");
            using (resource.Stream)
            using (var player = new System.Media.SoundPlayer(resource.Stream)) player.Load();
        }
        typeof(MainWindow).Assembly.GetType("PowerControlProWpf.UiSoundService", true)!
            .GetProperty("MuteForTests", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, true);
        var sizes = new[] { (860.0, 620.0), (900.0, 650.0), (1050.0, 760.0), (1200.0, 870.0) };
        for (var sizeIndex = 0; sizeIndex < sizes.Length; sizeIndex++)
        {
            var window = new MainWindow
            {
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            typeof(MainWindow).GetProperty("SkipSavedPlacementForTests", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(window, true);
            ((ComboBox)window.FindName("SizeBox")!).SelectedIndex = sizeIndex;
            (window.Width, window.Height) = sizes[sizeIndex];
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            window.UpdateLayout();
            var homeBottomHeight = ((Grid)window.FindName("HomePage")!).RowDefinitions[1].ActualHeight;

            var logo = (FrameworkElement)window.FindName("BrandLogo")!;
            var footer = (FrameworkElement)window.FindName("FooterLayout")!;
            if (logo.ActualWidth < 36 || logo.ActualHeight < 36 || footer.ActualHeight < logo.ActualHeight)
                throw new InvalidOperationException($"Logo is clipped at size {sizeIndex}.");

            var notice = (Button)window.FindName("UpdateNoticeButton")!;
            window.UpdateLayout();
            var title = (FrameworkElement)window.FindName("TitleProText")!;
            var header = (FrameworkElement)window.FindName("HeaderLayout")!;
            var titleRight = title.TransformToAncestor(header).Transform(new Point(title.ActualWidth, 0)).X;
            var noticeLeft = notice.TransformToAncestor(header).Transform(new Point()).X;
            if (titleRight > noticeLeft)
                throw new InvalidOperationException($"Header overlaps the update bell at size {sizeIndex}.");
            var admin = (Button)window.FindName("AdminRightsBadge")!;
            var adminRight = admin.TransformToAncestor(header).Transform(new Point(admin.ActualWidth, 0)).X;
            var titleLeft = ((FrameworkElement)window.FindName("HeaderIcon")!).TransformToAncestor(header).Transform(new Point()).X;
            if (adminRight > titleLeft)
                throw new InvalidOperationException($"Admin badge overlaps the title at size {sizeIndex}.");
            if (sizeIndex == 2)
            {
                typeof(MainWindow).GetField("_english", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
                typeof(MainWindow).GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, ["Standard"]);
                var log = (ListBox)window.FindName("LogBox")!;
                log.Items.Clear();
                log.Items.Add("[14.10.2026, 10:00:03] Benutzerübersicht aktualisiert.");
                log.Items.Add("[14.10.2026, 10:00:00] PowerLeitstand PRO WPF gestartet.");
                window.UpdateLayout();
                Capture(window, Path.Combine(args[0], "home-readme.png"));
            }
            VerifyUpdateStates(window);

            if (sizeIndex == 0)
                Capture(window, Path.Combine(args[0], "home-small.png"));
            typeof(MainWindow).GetMethod("ToggleSettings", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [null, new RoutedEventArgs()]);
            window.UpdateLayout();
            var options = (FrameworkElement)window.FindName("SettingsPanel")!;
            var content = (FrameworkElement)window.FindName("HomePage")!;
            if (options.ActualHeight > content.ActualHeight || options.ActualWidth > content.ActualWidth)
                throw new InvalidOperationException($"Options dialog is clipped at size {sizeIndex}.");
            if (sizeIndex == 0)
            {
                Capture(window, Path.Combine(args[0], "options-small.png"));
                typeof(MainWindow).GetField("_english", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
                typeof(MainWindow).GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                window.UpdateLayout();
                Capture(window, Path.Combine(args[0], "options-english-small.png"));
                var englishEditor = typeof(MainWindow).Assembly.GetType("PowerControlProWpf.UserEditorDialog", true)!;
                var englishConstructor = englishEditor.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                CaptureDialog((Window)englishConstructor.Invoke([window, null]), Path.Combine(args[0], "user-create-english.png"));
                CaptureDialog((Window)englishConstructor.Invoke([window, new UserEntry("demo", "Standard user", "Demo account", "Active", true, "Demo")]),
                    Path.Combine(args[0], "user-edit-english.png"));
                var resetType = typeof(MainWindow).Assembly.GetType("PowerControlProWpf.PasswordResetDialog", true)!;
                var resetConstructor = resetType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                CaptureDialog((Window)resetConstructor.Invoke([window, "demo", true]), Path.Combine(args[0], "password-reset-english.png"));
                typeof(MainWindow).GetField("_english", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
                typeof(MainWindow).GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            }
            typeof(MainWindow).GetMethod("ToggleSettings", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [null, new RoutedEventArgs()]);
            typeof(MainWindow).GetMethod("OpenUpdateNotice", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [null, new RoutedEventArgs()]);
            window.UpdateLayout();
            var settingsShell = (FrameworkElement)window.FindName("SettingsShell")!;
            var noticePanel = (FrameworkElement)window.FindName("SettingsNoticePanel")!;
            if (settingsShell.Visibility != Visibility.Collapsed || options.Visibility != Visibility.Collapsed ||
                noticePanel.Visibility != Visibility.Visible || noticePanel.ActualWidth > content.ActualWidth ||
                noticePanel.ActualHeight > content.ActualHeight)
                throw new InvalidOperationException($"Update notice is clipped at size {sizeIndex}.");
            if (noticePanel.ActualWidth > 360 ||
                ((FrameworkElement)window.FindName("NoticeCurrentRow")!).ActualWidth < noticePanel.ActualWidth - 30)
                throw new InvalidOperationException($"Update notice dialog layout is invalid at size {sizeIndex}.");
            if (sizeIndex == 0)
            {
                Capture(window, Path.Combine(args[0], "update-notice-small.png"));
                typeof(MainWindow).GetField("_english", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
                typeof(MainWindow).GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
                window.UpdateLayout();
                if (noticePanel.ActualWidth > 360 || noticePanel.ActualHeight > content.ActualHeight)
                    throw new InvalidOperationException("English update notice is clipped at small size.");
                Capture(window, Path.Combine(args[0], "update-notice-english-small.png"));
                typeof(MainWindow).GetField("_english", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
                typeof(MainWindow).GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            }
            typeof(MainWindow).GetMethod("CloseSettingsNotice", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, [null, new RoutedEventArgs()]);
            typeof(MainWindow).GetMethod("ShowUsersPage", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);
            window.UpdateLayout();
            var userBottomHeight = ((Grid)window.FindName("UserPage")!).RowDefinitions[1].ActualHeight;
            if (Math.Abs(userBottomHeight - homeBottomHeight) > 1)
                throw new InvalidOperationException($"Status and log shift between pages 1 and 2 at size {sizeIndex}.");
            var userLog = (FrameworkElement)window.FindName("UserLogBox")!;
            if (userLog.ActualHeight < 130)
                throw new InvalidOperationException($"Page-two log is too short at size {sizeIndex}: {userLog.ActualHeight:0}.");
            var users = (ListView)window.FindName("UserList")!;
            users.Items.Clear();
            users.Items.Add(new UserEntry("Administrator", "Administrator", "Integriertes Administratorkonto", "Aktiv", true));
            users.Items.Add(new UserEntry("DefaultAccount", "Standardbenutzer", "Von Windows verwaltetes Standardkonto", "Deaktiviert", false));
            users.Items.Add(new UserEntry("Gast", "Standardbenutzer", "Integriertes Gastkonto", "Deaktiviert", false));
            users.Items.Add(new UserEntry("rx8boss", "Administrator", "Lokales Benutzerkonto", "Aktiv", true));
            users.Items.Add(new UserEntry("WDAGUtilityAccount", "Standardbenutzer", "Von Windows verwaltetes Konto (Windows Defender Application Guard)", "Deaktiviert", false));
            users.SelectedIndex = 3;
            ((Button)window.FindName("RefreshUsersButton")!).IsEnabled = true;
            window.UpdateLayout();
            var userColumns = ((GridView)users.View).Columns;
            if (userColumns.Sum(column => column.ActualWidth) > users.ActualWidth ||
                userColumns[3].ActualWidth < 90)
                throw new InvalidOperationException($"User overview columns are clipped at size {sizeIndex}.");
            if (sizeIndex == 0)
            {
                Capture(window, Path.Combine(args[0], "users-small.png"));
                var refresh = (FrameworkElement)window.FindName("RefreshUsersButton")!;
                var usersPage = (FrameworkElement)window.FindName("UserPage")!;
                var refreshRight = refresh.TransformToAncestor(usersPage).Transform(new Point(refresh.ActualWidth, 0)).X;
                if (refreshRight > usersPage.ActualWidth)
                    throw new InvalidOperationException("User action buttons are clipped at small size.");
                var editorType = typeof(MainWindow).Assembly.GetType("PowerControlProWpf.UserEditorDialog", true)!;
                var constructor = editorType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                CaptureDialog((Window)constructor.Invoke([window, null]), Path.Combine(args[0], "user-create.png"));
                CaptureDialog((Window)constructor.Invoke([window, new UserEntry("rx8boss", "Standardbenutzer", "Administrator für Systemwartung", "Aktiv", true, "RX8 BOSS")]),
                    Path.Combine(args[0], "user-edit.png"));
                VerifyUserEditor((Window)constructor.Invoke([window, null]),
                    (Window)constructor.Invoke([window, new UserEntry("demo", "Standardbenutzer", "Testkonto", "Aktiv", true, "Demo")]));
                var resetType = typeof(MainWindow).Assembly.GetType("PowerControlProWpf.PasswordResetDialog", true)!;
                var resetConstructor = resetType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                CaptureDialog((Window)resetConstructor.Invoke([window, "rx8boss", false]), Path.Combine(args[0], "password-reset.png"));
                VerifyPasswordResetValidation((Window)resetConstructor.Invoke([window, "demo", false]));
                VerifyPasswordRemovalChoice((Window)resetConstructor.Invoke([window, "demo", false]),
                    Path.Combine(args[0], "password-remove.png"));
                VerifyPasswordVisibility((Window)constructor.Invoke([window, null]));
                VerifyThemes(window, args[0]);
            }
            typeof(MainWindow).GetMethod("ShowRepairPage", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null);
            window.UpdateLayout();
            var repairBottomHeight = ((Grid)window.FindName("RepairPage")!).RowDefinitions[1].ActualHeight;
            if (Math.Abs(repairBottomHeight - homeBottomHeight) > 1)
                throw new InvalidOperationException($"Status and log shift between pages 1 and 3 at size {sizeIndex}.");
            if (sizeIndex == 0)
            {
                Capture(window, Path.Combine(args[0], "repair-small.png"));
                CaptureRepairDialogs(args[0]);
            }
            window.Close();
            Console.WriteLine($"PASS layout {sizes[sizeIndex].Item1:0}x{sizes[sizeIndex].Item2:0}: footer, bell, user log");
        }
        app.Shutdown();
        return 0;
    }

    private static void Capture(Window window, string file)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file);
        encoder.Save(output);
    }

    private static void CaptureDialog(Window dialog, string file)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = -10000;
        dialog.Top = -10000;
        dialog.ShowActivated = false;
        dialog.Show();
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        dialog.UpdateLayout();
        Capture(dialog, file);
        dialog.Close();
    }

    private static void CaptureRepairDialogs(string output)
    {
        var assembly = typeof(MainWindow).Assembly;
        var paletteType = assembly.GetType("PowerControlProWpf.ThemePalette", true)!;
        var palette = paletteType.GetMethod("For", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, ["Standard"]);
        var summaryType = assembly.GetType("PowerControlProWpf.RepairResultSummary", true)!;
        var summary = summaryType.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, ["sfc", 0, false, "Keine Integritätsverletzungen gefunden", "", TimeSpan.FromMinutes(6), false]);
        var resultType = typeof(MainWindow).Assembly.GetType("PowerControlProWpf.RepairResultWindow", true)!;
        var resultConstructor = resultType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        CaptureDialog((Window)resultConstructor.Invoke([summary, palette, false]), Path.Combine(output, "repair-result.png"));
        var errorSummary = summaryType.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, ["dism", 87, false, "", "Fehlende Reparaturquelle", TimeSpan.FromSeconds(25), false]);
        CaptureDialog((Window)resultConstructor.Invoke([errorSummary, palette, false]), Path.Combine(output, "repair-result-error.png"));
        foreach (var theme in new[] { "Classic", "Dark", "Neon Pink" })
        {
            var themePalette = paletteType.GetMethod("For", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [theme]);
            CaptureDialog((Window)resultConstructor.Invoke([summary, themePalette, false]),
                Path.Combine(output, $"repair-result-{theme.Replace(' ', '-')}.png"));
        }
        var confirmType = assembly.GetType("PowerControlProWpf.RepairConfirmWindow", true)!;
        var confirmConstructor = confirmType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        CaptureDialog((Window)confirmConstructor.Invoke(["sfc /scannow", palette, false]), Path.Combine(output, "repair-confirm.png"));
        var classicPalette = paletteType.GetMethod("For", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["Classic"]);
        CaptureDialog((Window)confirmConstructor.Invoke(["DISM /Online /Cleanup-Image /RestoreHealth", classicPalette, false]),
            Path.Combine(output, "repair-confirm-Classic.png"));
        var messageType = assembly.GetType("PowerControlProWpf.ThemedMessageBoxWindow", true)!;
        var messageConstructor = messageType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        CaptureDialog((Window)messageConstructor.Invoke(["Ereignisprotokoll unwiderruflich löschen?", "Ereignisprotokoll löschen?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, palette, false]), Path.Combine(output, "confirm-delete.png"));
        CaptureDialog((Window)messageConstructor.Invoke(["Ereignisprotokoll unwiderruflich löschen?", "Ereignisprotokoll löschen?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, classicPalette, false]), Path.Combine(output, "confirm-delete-Classic.png"));
        var logType = assembly.GetType("PowerControlProWpf.RepairLogWindow", true)!;
        var logConstructor = logType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var logEntryType = assembly.GetType("PowerControlProWpf.RepairLogEntry", true)!;
        var emptyEntries = Array.CreateInstance(logEntryType, 0);
        CaptureDialog((Window)logConstructor.Invoke([emptyEntries, palette, false, null]), Path.Combine(output, "repair-log.png"));
    }

    private static void VerifyPasswordVisibility(Window dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = -10000;
        dialog.Top = -10000;
        dialog.ShowActivated = false;
        dialog.Show();
        var field = (PasswordBox)dialog.GetType().GetField("_password", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        field.Password = "OnlyForUiTest123!";
        var eye = VisualChildren<Button>(dialog).First(button => (button.ToolTip as string) == "Kennwort anzeigen");
        eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, eye));
        if (field.Visibility != Visibility.Collapsed ||
            !VisualChildren<TextBox>(dialog).Any(box => box.Visibility == Visibility.Visible && box.Text == "OnlyForUiTest123!"))
            throw new InvalidOperationException("Password reveal button does not display the entered value.");
        eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, eye));
        if (field.Visibility != Visibility.Visible || field.Password != "OnlyForUiTest123!" ||
            VisualChildren<TextBox>(dialog).Any(box => box.Text == "OnlyForUiTest123!"))
            throw new InvalidOperationException("Password reveal button did not restore masking.");
        dialog.Close();
    }

    private static void VerifyUserEditor(Window create, Window edit)
    {
        foreach (var dialog in new[] { create, edit })
        {
            var root = (Border)dialog.Content;
            var layout = (Grid)root.Child;
            var footer = (Border)layout.Children.Cast<UIElement>().Single(child => Grid.GetRow(child) == 3);
            var actions = (Grid)footer.Child;
            if (!ReferenceEquals(root.Background, ((MainWindow)dialog.Owner).TryFindResource("CardSurfaceBrush")) ||
                root.Padding.Bottom < 10 || layout.RowDefinitions[3].Height.Value < 50 || actions.Margin.Bottom < 4)
                throw new InvalidOperationException("User dialog does not match the card surface or has cramped footer buttons.");
        }
        create.WindowStartupLocation = WindowStartupLocation.Manual;
        create.Left = -10000;
        create.Top = -10000;
        create.ShowActivated = false;
        create.Show();
        create.UpdateLayout();
        var fields = create.GetType();
        var noPassword = (CheckBox)fields.GetField("_withoutPassword", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(create)!;
        var password = (PasswordBox)fields.GetField("_password", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(create)!;
        var repeat = (PasswordBox)fields.GetField("_repeat", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(create)!;
        password.Password = "OnlyForUiTest123!";
        noPassword.IsChecked = true;
        if (password.Password.Length != 0 || repeat.Password.Length != 0 || password.IsEnabled || repeat.IsEnabled)
            throw new InvalidOperationException("Passwordless mode did not clear and disable both password fields.");
        noPassword.IsChecked = false;
        if (!password.IsEnabled || !repeat.IsEnabled)
            throw new InvalidOperationException("Password fields did not return after disabling passwordless mode.");
        create.Close();

        edit.WindowStartupLocation = WindowStartupLocation.Manual;
        edit.Left = -10000;
        edit.Top = -10000;
        edit.ShowActivated = false;
        edit.Show();
        edit.UpdateLayout();
        var name = (TextBox)fields.GetField("_name", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(edit)!;
        if (name.IsReadOnly) throw new InvalidOperationException("The account name is still read-only.");
        var reset = VisualChildren<Button>(edit).First(button =>
            VisualChildren<TextBlock>(button).Any(text => text.Text == "Kennwort zurücksetzen"));
        var delete = VisualChildren<Button>(edit).First(button =>
            VisualChildren<TextBlock>(button).Any(text => text.Text == "Löschen"));
        if (Math.Abs(reset.ActualWidth - delete.ActualWidth) > 2 || reset.ActualWidth < 140)
            throw new InvalidOperationException("Reset and delete buttons do not have equal usable widths.");
        foreach (var button in new[] { reset, delete })
        {
            var content = (FrameworkElement)button.Content;
            if (content.ActualWidth > button.ActualWidth - 6)
                throw new InvalidOperationException("An account action label is clipped.");
        }
        edit.Close();
        Console.WriteLine("PASS user dialogs: rename field, equal action buttons and passwordless switch");
    }

    private static void VerifyPasswordResetValidation(Window dialog)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = -10000;
        dialog.Top = -10000;
        dialog.ShowActivated = false;
        var type = dialog.GetType();
        var first = (PasswordBox)type.GetField("_first", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var second = (PasswordBox)type.GetField("_second", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var error = (TextBlock)type.GetField("_error", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var confirm = type.GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var validated = false;
        dialog.Dispatcher.BeginInvoke(new Action(() =>
        {
            confirm.Invoke(dialog, [null, new RoutedEventArgs()]);
            if (dialog.DialogResult == true || error.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Empty password was accepted.");
            first.Password = "LocalUiTest123!";
            var eye = VisualChildren<Button>(dialog).First(button => (button.ToolTip as string) == "Kennwort anzeigen");
            eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, eye));
            if (first.Visibility != Visibility.Collapsed ||
                !VisualChildren<TextBox>(dialog).Any(box => box.Text == "LocalUiTest123!"))
                throw new InvalidOperationException("Reset dialog did not reveal the password.");
            eye.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, eye));
            if (first.Visibility != Visibility.Visible || first.Password != "LocalUiTest123!" ||
                VisualChildren<TextBox>(dialog).Any(box => box.Text == "LocalUiTest123!"))
                throw new InvalidOperationException("Reset dialog did not mask the password again.");
            second.Password = "DifferentUiTest123!";
            confirm.Invoke(dialog, [null, new RoutedEventArgs()]);
            if (dialog.DialogResult == true || !error.Text.Contains("stimmen nicht", StringComparison.Ordinal))
                throw new InvalidOperationException("Mismatched passwords were accepted.");
            second.Password = first.Password;
            confirm.Invoke(dialog, [null, new RoutedEventArgs()]);
            validated = true;
        }), DispatcherPriority.Loaded);
        var result = type.GetProperty("Result", BindingFlags.NonPublic | BindingFlags.Instance)!;
        if (dialog.ShowDialog() != true || !validated ||
            !IsPasswordChoice(result.GetValue(dialog), "LocalUiTest123!", false))
            throw new InvalidOperationException("Matching password did not produce a result.");
    }

    private static void VerifyPasswordRemovalChoice(Window dialog, string imagePath)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = -10000;
        dialog.Top = -10000;
        dialog.ShowActivated = false;
        var type = dialog.GetType();
        var first = (PasswordBox)type.GetField("_first", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var second = (PasswordBox)type.GetField("_second", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var remove = (CheckBox)type.GetField("_removePassword", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var fields = (StackPanel)type.GetField("_passwordFields", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        var confirm = type.GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var validated = false;
        dialog.Dispatcher.BeginInvoke(new Action(() =>
        {
            first.Password = "OnlyForUiTest123!";
            second.Password = "OnlyForUiTest123!";
            remove.IsChecked = true;
            if (first.Password.Length != 0 || second.Password.Length != 0 || fields.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Password removal did not clear and hide existing input.");
            dialog.UpdateLayout();
            Capture(dialog, imagePath);
            remove.IsChecked = false;
            if (fields.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Password fields did not return after deselecting removal.");
            remove.IsChecked = true;
            confirm.Invoke(dialog, [null, new RoutedEventArgs()]);
            validated = true;
        }), DispatcherPriority.Loaded);
        var result = type.GetProperty("Result", BindingFlags.NonPublic | BindingFlags.Instance)!;
        if (dialog.ShowDialog() != true || !validated ||
            !IsPasswordChoice(result.GetValue(dialog), "", true))
            throw new InvalidOperationException("Password removal did not produce the explicit removal result.");
        Console.WriteLine("PASS password removal choice: clear input, hide fields and distinct result");
    }

    private static bool IsPasswordChoice(object? choice, string password, bool removePassword) =>
        choice is not null &&
        (string?)choice.GetType().GetProperty("Password")?.GetValue(choice) == password &&
        (bool?)choice.GetType().GetProperty("RemovePassword")?.GetValue(choice) == removePassword;

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void VerifyUpdateStates(MainWindow window)
    {
        var enumType = typeof(MainWindow).GetNestedType("UpdateCheckState", BindingFlags.NonPublic)!;
        var setState = typeof(MainWindow).GetMethod("SetUpdateCheckState", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var footer = (TextBlock)window.FindName("FooterUpdateStatus")!;
        var bell = (System.Windows.Shapes.Path)window.FindName("UpdateNoticeIcon")!;
        var optionsButton = (Button)window.FindName("CheckUpdatesButton")!;
        var optionsLabel = (TextBlock)window.FindName("CheckUpdatesLabel")!;
        var detail = (TextBlock)window.FindName("SettingsNoticeDetail")!;
        if (window.FindName("SettingsNoticeAction") is not null)
            throw new InvalidOperationException("The update notice must not contain a check button.");
        var rows = new[]
        {
            (Border)window.FindName("NoticeCurrentRow")!,
            (Border)window.FindName("NoticeConnectionRow")!,
            (Border)window.FindName("NoticeAvailableRow")!
        };
        var colors = new List<System.Windows.Media.Color>();
        var buttonColors = new List<System.Windows.Media.Color>();
        foreach (var (state, expected, activeRow) in new[] { ("Current", "Aktuell", 0), ("Failed", "Prüfung fehlgeschlagen", 1), ("Available", "Update", 2) })
        {
            setState.Invoke(window, [Enum.Parse(enumType, state)]);
            if (!footer.Text.Contains(expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected footer update state: {state}: {footer.Text}");
            if (rows[activeRow].Opacity != 1 || rows.Where((_, index) => index != activeRow).Any(row => row.Opacity >= 1))
                throw new InvalidOperationException($"Wrong active bell in update notice: {state}.");
            var statusColor = ((System.Windows.Media.SolidColorBrush)bell.Stroke).Color;
            colors.Add(statusColor);
            if (((System.Windows.Media.SolidColorBrush)optionsButton.BorderBrush).Color != statusColor)
                throw new InvalidOperationException($"Options update button color differs from bell: {state}.");
            buttonColors.Add(((System.Windows.Media.SolidColorBrush)optionsButton.Background).Color);
            if (optionsLabel.FontSize < 11 || string.IsNullOrWhiteSpace(optionsLabel.Text))
                throw new InvalidOperationException("Options update button label is too small or missing.");
            if (state == "Current" && detail.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Current notice contains an unnecessary description.");
        }
        if (colors.Distinct().Count() != 3)
            throw new InvalidOperationException("Update bell colors are not distinct.");
        if (buttonColors.Distinct().Count() != 3)
            throw new InvalidOperationException("Options update button backgrounds are not distinct.");
        setState.Invoke(window, [Enum.Parse(enumType, "Checking")]);
        if (footer.Text == "Aktuell")
            throw new InvalidOperationException("Checking state must not say current.");
    }

    private static void VerifyThemes(MainWindow window, string output)
    {
        var applyTheme = typeof(MainWindow).GetMethod("ApplyTheme", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var setOverlay = typeof(MainWindow).GetMethod("SetSettingsOverlay", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var switchPage = typeof(MainWindow).GetMethod("SwitchPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var showUsers = typeof(MainWindow).GetMethod("ShowUsersPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var showRepair = typeof(MainWindow).GetMethod("ShowRepairPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var theme in new[] { "Standard", "Neon Blau", "Neon Pink", "Neon Grün", "Retro", "Linux Mint", "Graphit", "Classic", "Dark" })
        {
            applyTheme.Invoke(window, [theme]);
            var timerCorner = (System.Windows.Shapes.Path)window.FindName("TimerCardCorner")!;
            if (Grid.GetRow(timerCorner) != 0 || Grid.GetColumn(timerCorner) != 1 ||
                timerCorner.Visibility != Visibility.Visible)
                throw new InvalidOperationException($"Timer card corner is missing or inconsistent in {theme}.");
            VerifyUpdateStates(window);
            setOverlay.Invoke(window, [true, false]);
            if (theme == "Standard")
            {
                var enumType = typeof(MainWindow).GetNestedType("UpdateCheckState", BindingFlags.NonPublic)!;
                var setState = typeof(MainWindow).GetMethod("SetUpdateCheckState", BindingFlags.NonPublic | BindingFlags.Instance)!;
                foreach (var state in new[] { "Current", "Failed", "Available" })
                {
                    setState.Invoke(window, [Enum.Parse(enumType, state)]);
                    window.UpdateLayout();
                    Capture(window, Path.Combine(output, $"options-update-{state.ToLowerInvariant()}.png"));
                }
                setState.Invoke(window, [Enum.Parse(enumType, "Checking")]);
            }
            window.UpdateLayout();
            var rows = (Grid)window.FindName("SettingsRows")!;
            foreach (var labelName in new[] { "LanguageTitle", "ThemeTitle", "SizeTitle", "WarningTitle", "WarningSoundTitle", "WarningVolumeTitle", "AutoStartTitle", "ElevationTitle", "UpdateTitle" })
            {
                var label = (TextBlock)window.FindName(labelName)!;
                var right = label.TransformToAncestor(rows).Transform(new Point(label.ActualWidth, 0)).X;
                if (right > rows.ColumnDefinitions[0].ActualWidth - 1)
                    throw new InvalidOperationException($"{labelName} clipped in {theme}: {right:0.0} > {rows.ColumnDefinitions[0].ActualWidth:0.0}.");
            }
            Capture(window, Path.Combine(output, $"options-{theme.Replace(' ', '-')}.png"));
            setOverlay.Invoke(window, [false, true]);
            window.UpdateLayout();
            Capture(window, Path.Combine(output, $"notice-{theme.Replace(' ', '-')}.png"));
            setOverlay.Invoke(window, [false, false]);
            showUsers.Invoke(window, null);
            window.UpdateLayout();
            Capture(window, Path.Combine(output, $"users-{theme.Replace(' ', '-')}.png"));
            showRepair.Invoke(window, null);
            window.UpdateLayout();
            Capture(window, Path.Combine(output, $"repair-{theme.Replace(' ', '-')}.png"));
            var previous = (Button)window.FindName("PagePrevious")!;
            switchPage.Invoke(window, [previous, new RoutedEventArgs()]);
            switchPage.Invoke(window, [previous, new RoutedEventArgs()]);
            window.UpdateLayout();
            Capture(window, Path.Combine(output, $"home-{theme.Replace(' ', '-')}.png"));
            showUsers.Invoke(window, null);
            var editorType = typeof(MainWindow).Assembly.GetType("PowerControlProWpf.UserEditorDialog", true)!;
            var constructor = editorType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            CaptureDialog((Window)constructor.Invoke([window, null]), Path.Combine(output, $"user-create-{theme.Replace(' ', '-')}.png"));
            CaptureDialog((Window)constructor.Invoke([window, new UserEntry("demo", "Standardbenutzer", "Testkonto", "Aktiv", true, "Demo")]),
                Path.Combine(output, $"user-edit-{theme.Replace(' ', '-')}.png"));
            var resetType = typeof(MainWindow).Assembly.GetType("PowerControlProWpf.PasswordResetDialog", true)!;
            var resetConstructor = resetType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            CaptureDialog((Window)resetConstructor.Invoke([window, "demo", false]),
                Path.Combine(output, $"password-reset-{theme.Replace(' ', '-')}.png"));
            Console.WriteLine($"PASS options theme {theme}");
        }
        setOverlay.Invoke(window, [false, false]);
    }
}
