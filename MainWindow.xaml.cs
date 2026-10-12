using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;
using MessageBox = PowerControlProWpf.ThemedMessageBox;

namespace PowerControlProWpf;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _seconds = 300;
    private DateTimeOffset _timerDeadline;
    private int _nextWarningSecond = 10;
    private bool _finalWarningPlayed;
    private double _warningPlaybackFactor = 1;
    private string? _timerAction;
    private int _pageIndex;
    private bool _english;
    private bool _repairRunning;
    private CancellationTokenSource? _repairCancellation;
    private double _repairPercent;
    private string? _repairCommand;
    private MediaPlayer? _warningPlayer;
    private readonly Dictionary<TextBlock, double> _baseTextSizes = new();
    private readonly Dictionary<Button, double> _baseButtonSizes = new();
    private readonly Dictionary<Image, (double Width, double Height)> _baseImageSizes = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private const string AutoStartKey = "PowerControlProWpf";
    private readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power Control PRO", "logs");
    private readonly object _logLock = new();
    private string LogFilePath => Path.Combine(_logDirectory, "events.log");
    private string RepairLogFilePath => Path.Combine(_logDirectory, "repair.log");
    internal bool IsTimerActive => _timer.IsEnabled;
    internal bool IsBusy => _timer.IsEnabled || _repairRunning;
    internal bool IsEnglish => _english;
    internal string CurrentThemeName => SelectedTheme();
    internal string CurrentIconSet => ThemePalette.For(SelectedTheme()).IconSet;
    internal string CurrentFontFamily => ThemePalette.For(SelectedTheme()).FontFamily;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += TimerTick;
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                Dispatcher.BeginInvoke(ShowPendingRepairSummary);
        };
        LoadSettings();
        LoadAutoStart();
        ApplyLanguage();
        Loaded += (_, _) => { ApplyTheme(SelectedTheme()); ApplyLayoutScale(); };
        LoadPersistedLog();
        LoadPersistedRepairLog();
        Log("Power Control PRO WPF gestartet.");
        LoadUsers(null, null!);
    }

    private static string ThemeId(ComboBoxItem item) => item.Tag?.ToString() ?? item.Content?.ToString() ?? "Standard";
    private string SelectedTheme() => ThemeBox.SelectedItem is ComboBoxItem item ? ThemeId(item) : "Standard";

    private void LoadSettings()
    {
        _english = _settings.Language == "en";
        ThemeBox.SelectedItem = ThemeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => ThemeId(item) == _settings.Theme) ?? ThemeBox.Items[0];
        SizeBox.SelectedIndex = Math.Clamp(_settings.SizeIndex, 0, SizeBox.Items.Count - 1);
        WarningSound.IsChecked = _settings.WarningEnabled;
        WarningSoundBox.SelectedItem = WarningSoundBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == _settings.WarningSound) ?? WarningSoundBox.Items[0];
        WarningVolume.Value = Math.Clamp(_settings.WarningVolume, 0, 100);
        ApplySize();
    }

    private void SaveSettings()
    {
        if (!IsLoaded) return;
        _settings.Language = _english ? "en" : "de";
        _settings.Theme = SelectedTheme();
        _settings.SizeIndex = SizeBox.SelectedIndex;
        _settings.WarningEnabled = WarningSound.IsChecked == true;
        _settings.WarningSound = (WarningSoundBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system-warning.wav";
        _settings.WarningVolume = (int)Math.Round(WarningVolume.Value);
        try { _settings.Save(); }
        catch (Exception exception) { Log((_english ? "Settings could not be saved: " : "Einstellungen konnten nicht gespeichert werden: ") + exception.Message); }
    }

    private void SaveSettingsChanged(object s, RoutedEventArgs e) => SaveSettings();
    private void WarningSoundChanged(object s, SelectionChangedEventArgs e) => SaveSettings();
    private void WarningVolumeChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (WarningVolumeValue is null) return;
        WarningVolumeValue.Text = $"{(int)Math.Round(e.NewValue)}%";
        if (_warningPlayer is not null) _warningPlayer.Volume = e.NewValue / 100 * _warningPlaybackFactor;
        SaveSettings();
    }

    private void ApplySize()
    {
        (Width, Height) = SizeBox.SelectedIndex switch { 0 => (860, 620), 1 => (900, 650), 2 => (1050, 760), _ => (1200, 870) };
        if (IsLoaded) ApplyLayoutScale();
    }

    private void ApplyLayoutScale()
    {
        // Re-layout the controls at their final size. A Viewbox/ScaleTransform would raster-scale the glyphs.
        var factor = SizeBox.SelectedIndex switch { 2 => 1050.0 / 900, 3 => 1200.0 / 900, _ => 1.0 };
        var compact = SizeBox.SelectedIndex == 0;
        Root.RowDefinitions[0].Height = new GridLength(90 * factor);
        Root.RowDefinitions[2].Height = new GridLength(24 * factor);
        Root.RowDefinitions[3].Height = new GridLength(32 * factor);
        HeaderLayout.ColumnDefinitions[0].Width = new GridLength(145 * factor);
        HeaderLayout.ColumnDefinitions[2].Width = new GridLength(155 * factor);

        foreach (var textBlock in FindVisualChildren<TextBlock>(Root))
        {
            if (IsInside(textBlock, SettingsPanel) || IsInside(textBlock, LogBox) || IsInside(textBlock, UserLogBox) || IsInside(textBlock, RepairLogBox) || IsInside(textBlock, UserList)) continue;
            if (!_baseTextSizes.TryGetValue(textBlock, out var baseSize))
                _baseTextSizes[textBlock] = baseSize = textBlock.FontSize;
            textBlock.FontSize = Math.Round(baseSize * factor);
        }
        foreach (var button in FindVisualChildren<Button>(Root))
        {
            if (IsInside(button, SettingsPanel) || IsInside(button, LogBox) || IsInside(button, UserLogBox) || IsInside(button, RepairLogBox) || IsInside(button, UserList)) continue;
            if (!_baseButtonSizes.TryGetValue(button, out var baseSize))
                _baseButtonSizes[button] = baseSize = button.FontSize;
            button.FontSize = Math.Round(baseSize * factor);
            if (ReferenceEquals(button.Style, Resources["HeaderButton"]) || ReferenceEquals(button.Style, Resources["CloseButtonStyle"]))
            {
                var isPageArrow = ReferenceEquals(button, PagePrevious) || ReferenceEquals(button, PageNext);
                button.Width = (isPageArrow ? 28 : 42) * factor;
                button.Height = (isPageArrow ? 28 : 36) * factor;
            }
        }
        PageIndicator.Width = 36 * factor;
        foreach (var icon in FindVisualChildren<Image>(Root))
        {
            if (IsInside(icon, SettingsPanel)) continue;
            if (!_baseImageSizes.TryGetValue(icon, out var baseSize))
                _baseImageSizes[icon] = baseSize = (icon.Width, icon.Height);
            icon.Width = baseSize.Width * factor;
            icon.Height = baseSize.Height * factor;
        }

        foreach (var button in new[] { ShutdownButton, RestartButton, LogoutButton, LockButton, TimerShutdown, TimerLock, TimerRestart })
            button.MinHeight = (compact ? 62 : 67) * factor;
        foreach (var label in new[] { TimerShutdownLabel, TimerLockLabel, TimerRestartLabel })
            label.FontSize = Math.Round((compact ? 11 : 12) * factor);
        foreach (var subtitle in new[] { TimerShutdownSub, TimerLockSub, TimerRestartSub })
            subtitle.FontSize = Math.Round((compact ? 9 : 10) * factor);
        AbortTimerButton.MinHeight = 48 * factor;
        foreach (var button in new[] { OpenLogButton, ClearLogButton, RefreshLogButton })
        {
            button.Width = double.NaN;
            button.Height = (compact ? 33 : 35) * factor;
        }
        LogBox.FontSize = 11 * factor;
        UserLogBox.FontSize = Math.Round(13 * factor);
        RepairLogBox.FontSize = Math.Round(14 * factor);
        UserList.FontSize = Math.Round(13 * factor);
        foreach (var sub in new[] { UsersSub, StatusSub, UserStatusHeaderSub, UserLogHeaderSub,
                     RepairHeaderSub, RepairStatusHeaderSub, RepairLogSub, UserStatusSub, RepairStatusSub })
            sub.FontSize = Math.Round(13 * factor);
        CurrentUserText.FontSize = Math.Round(14 * factor);
        CurrentUserRole.FontSize = Math.Round(12 * factor);
        RepairExplanation.FontSize = Math.Round(13 * factor);
        RepairProgressLabel.FontSize = Math.Round(14 * factor);
        RepairTimeText.FontSize = RepairResult.FontSize = Math.Round(13 * factor);
        RepairDismSub.FontSize = RepairSfcSub.FontSize = Math.Round(12 * factor);
        foreach (var button in new[] { RepairDismButton, RepairSfcButton, RepairCancelButton,
                     ResetPasswordButton, RefreshUsersButton, ClearRepairLogButton, OpenRepairLogButton })
            button.FontSize = Math.Round(12 * factor);
        LogBox.Height = (compact ? 112 : 120) * factor;
        UserLogBox.Height = (compact ? 134 : 150) * factor;
        RepairDismButton.Height = RepairSfcButton.Height = RepairCancelButton.Height = (compact ? 40 : 44) * factor;
        RepairActionRow.ColumnDefinitions[4].Width = new GridLength(140 * factor);
        RepairProgress.Height = 11 * factor;
        OpenRepairLogButton.Height = 30 * factor;
        ClearRepairLogButton.Height = 30 * factor;
        ShowRepairProtocolButton.Height = (compact ? 31 : 34) * factor;
        // Let the translated label determine the required width instead of clipping it.
        ShowRepairProtocolButton.Width = double.NaN;
        ShowRepairProtocolButton.MinWidth = (compact ? 174 : 222) * factor;
        ShowRepairProtocolButton.FontSize = Math.Round(12 * factor);
        UpdateRepairProtocolButtonLabel();
        UserList.Height = double.NaN;
        ResetPasswordButton.Width = 150 * factor;
        ResetPasswordButton.Height = RefreshUsersButton.Height = 28 * factor;
        RefreshUsersButton.Width = 105 * factor;
        foreach (var badge in new[] { StatusBadge, UserStatusBadge, RepairStatusBadge })
        {
            badge.Width = badge.Height = 54 * factor;
            badge.CornerRadius = new CornerRadius(27 * factor);
            if (badge.Parent is Grid badgeRow)
            {
                badgeRow.Height = 72 * factor;
                badgeRow.ColumnDefinitions[0].Width = new GridLength(64 * factor);
            }
        }
        if (UserList.View is GridView userColumns)
        {
            var widths = new[] { 180.0, 160, 260, 110 };
            for (var index = 0; index < Math.Min(userColumns.Columns.Count, widths.Length); index++)
                userColumns.Columns[index].Width = widths[index] * factor;
        }
        TimerSub.Width = SizeBox.SelectedIndex == 0 ? 105 : 124 * factor;
        if (TimerValue.Parent is StackPanel timerValueStack && timerValueStack.Parent is Border timerPicker && timerPicker.Parent is Grid timerHeader)
            timerHeader.ColumnDefinitions[1].Width = new GridLength(225 * factor);
        foreach (var card in FindVisualChildren<Border>(Root))
        {
            if (ReferenceEquals(card.Style, Resources["Card"])) card.Padding = new Thickness((compact ? 12 : 14) * factor);
        }
    }

    private static bool IsInside(DependencyObject child, DependencyObject ancestor)
    {
        for (var current = child; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private void UpdateRepairProtocolButtonLabel()
    {
        ShowRepairProtocolLabel.Text = SizeBox.SelectedIndex == 0
            ? (_english ? "View result" : "Protokoll anzeigen")
            : (_english ? "View repair result" : "Reparaturprotokoll anzeigen");
        ShowRepairProtocolButton.ToolTip = _english ? "Show the last repair result" : "Auswertung der letzten Reparatur anzeigen";
    }

    private void Log(string text)
    {
        var entry = $"[{DateTime.Now:dd.MM.yyyy, HH:mm:ss}] {text}";
        AddLogEntry(entry);
        try
        {
            lock (_logLock)
            {
                Directory.CreateDirectory(_logDirectory);
                File.AppendAllText(LogFilePath, entry + Environment.NewLine);
            }
        }
        catch
        {
            // Das Programm bleibt nutzbar, falls Windows den Logordner vorübergehend sperrt.
        }
    }

    private void AddLogEntry(string entry)
    {
        LogBox.Items.Insert(0, entry);
        UserLogBox.Items.Insert(0, entry);
        while (LogBox.Items.Count > 1000) LogBox.Items.RemoveAt(LogBox.Items.Count - 1);
        while (UserLogBox.Items.Count > 1000) UserLogBox.Items.RemoveAt(UserLogBox.Items.Count - 1);
    }

    private void LoadPersistedLog()
    {
        try
        {
            lock (_logLock)
            {
                if (!File.Exists(LogFilePath)) return;
                foreach (var entry in File.ReadLines(LogFilePath).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(1000).Reverse())
                    AddLogEntry(entry);
            }
        }
        catch
        {
            // Eine defekte alte Logdatei darf den Start der App nicht verhindern.
        }
    }

    private void ReloadPersistedLog()
    {
        LogBox.Items.Clear();
        UserLogBox.Items.Clear();
        LoadPersistedLog();
    }

    private void ShutdownNow(object s, RoutedEventArgs e) => ConfirmAndExecute(PowerAction.Shutdown);
    private void RestartNow(object s, RoutedEventArgs e) => ConfirmAndExecute(PowerAction.Restart);
    private void LogoutNow(object s, RoutedEventArgs e) => ConfirmAndExecute(PowerAction.SignOut);
    private void LockNow(object s, RoutedEventArgs e) => ConfirmAndExecute(PowerAction.Lock);

    private void ConfirmAndExecute(PowerAction action)
    {
        if (IsBusy)
        {
            MessageBox.Show(_english ? "Finish the active timer or repair first." : "Bitte zuerst den aktiven Timer oder die Reparatur beenden.", "Power Control PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var label = ActionLabel(action);
        var question = _english ? $"Run {label} now?" : $"{label} jetzt ausführen?";
        if (MessageBox.Show(question, "Power Control PRO", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        ExecuteAction(action);
    }

    private string ActionLabel(PowerAction action) => action switch
    {
        PowerAction.Shutdown => _english ? "shutdown" : "Herunterfahren",
        PowerAction.Restart => _english ? "restart" : "Neustart",
        PowerAction.SignOut => _english ? "sign out" : "Abmelden",
        _ => _english ? "lock Windows" : "Windows sperren"
    };

    private void ExecuteAction(PowerAction action)
    {
        try
        {
            PowerActionDispatcher.Execute(action, (file, arguments) =>
            {
                _ = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false })
                    ?? throw new InvalidOperationException("Der Windows-Befehl konnte nicht gestartet werden.");
            });
            Log((_english ? "Action started: " : "Aktion gestartet: ") + ActionLabel(action));
        }
        catch (Exception exception)
        {
            Log((_english ? "Action failed: " : "Aktion fehlgeschlagen: ") + exception.Message);
            MessageBox.Show((_english ? "Windows could not perform this action: " : "Windows konnte die Aktion nicht ausführen: ") + exception.Message, "Power Control PRO", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void StartTimerShutdown(object s, RoutedEventArgs e) => StartTimer("shutdown");
    private void StartTimerLock(object s, RoutedEventArgs e) => StartTimer("lock");
    private void StartTimerRestart(object s, RoutedEventArgs e) => StartTimer("restart");

    private void StartTimer(string action)
    {
        if (IsBusy) return;
        _ = TimerActionDispatcher.Resolve(action);
        _timerAction = action;
        _timerDeadline = DateTimeOffset.UtcNow.AddSeconds(_seconds);
        _nextWarningSecond = 10;
        _finalWarningPlayed = false;
        _timer.Start();
        SetTimerState(true);
        Log(_english ? $"Timer started: {TimerActionDispatcher.Label(action, true)} in {TimerLabel()}." : $"Timer gestartet: {TimerActionDispatcher.Label(action, false)} in {TimerLabel()}.");
    }

    private void AbortTimer(object s, RoutedEventArgs e)
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _warningPlayer?.Stop();
        _timerAction = null;
        SetTimerState(false);
        StatusDetail.Text = _english ? "No action active" : "Keine Aktion aktiv";
        TimerValue.Text = TimerLabel();
        Log(_english ? "Timer cancelled." : "Timer abgebrochen.");
    }

    private void TimerTick(object? s, EventArgs e)
    {
        var remaining = Math.Max(0, (int)Math.Ceiling((_timerDeadline - DateTimeOffset.UtcNow).TotalSeconds));
        if (remaining == 0)
        {
            var action = _timerAction;
            _timer.Stop();
            _warningPlayer?.Stop();
            _timerAction = null;
            SetTimerState(false);
            TimerValue.Text = TimerLabel();
            Log(_english ? "Timer completed." : "Timer abgelaufen.");
            if (action is not null) ExecuteAction(TimerActionDispatcher.Resolve(action));
            return;
        }
        if (WarningSound.IsChecked == true && remaining <= 5 && !_finalWarningPlayed)
        {
            _finalWarningPlayed = true;
            PlayWarningSound("urgent.wav", 1);
        }
        else if (WarningSound.IsChecked == true && remaining > 5 && remaining <= _nextWarningSecond && _nextWarningSecond >= 6)
        {
            _nextWarningSecond -= 2;
            PlayWarningSound(volumeFactor: 0.55);
        }
        var label = TimerLabel(remaining);
        TimerValue.Text = label;
        AbortCountdown.Text = (_english ? "Remaining: " : "Noch: ") + CountdownLabel(remaining);
        StatusDetail.Text = (_english ? "Timer active: " : "Timer aktiv: ") + label;
        UserStatusSub.Text = RepairStatusSub.Text = StatusDetail.Text;
    }

    private void SetTimerState(bool active)
    {
        (System.Windows.Application.Current as App)?.UpdateTrayTimerState(IsBusy);
        RepairDismButton.IsEnabled = RepairSfcButton.IsEnabled = !IsBusy;
        AbortTimerButton.IsEnabled = active;
        ShutdownButton.IsEnabled = RestartButton.IsEnabled = LogoutButton.IsEnabled = LockButton.IsEnabled = !IsBusy;
        TimerShutdown.IsEnabled = TimerLock.IsEnabled = TimerRestart.IsEnabled = !IsBusy;
        TimerMinusMin.IsEnabled = TimerPlusMin.IsEnabled = TimerMinusHourButton.IsEnabled = TimerPlusHourButton.IsEnabled = !IsBusy;
        AbortHint.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        AbortCountdown.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active) AbortCountdown.Text = (_english ? "Remaining: " : "Noch: ") + CountdownLabel(Math.Max(0, (int)Math.Ceiling((_timerDeadline - DateTimeOffset.UtcNow).TotalSeconds)));
        StatusText.Text = active ? (_english ? "Timer active" : "Timer aktiv")
            : _repairRunning ? (_english ? "Repair running" : "Reparatur läuft")
            : (_english ? "Ready" : "Bereit");
        UserStatusText.Text = RepairStatusText.Text = StatusText.Text;
        StatusSymbol.Text = UserStatusSymbol.Text = RepairStatusSymbol.Text = IsBusy ? "◷" : "✓";
        StatusBadge.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, IsBusy ? "WarningBrush" : "ReadyBrush");
        UserStatusBadge.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, IsBusy ? "WarningBrush" : "ReadyBrush");
        RepairStatusBadge.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, IsBusy ? "WarningBrush" : "ReadyBrush");
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, IsBusy ? "WarningBrush" : "ReadyBrush");
        UserStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, IsBusy ? "WarningBrush" : "ReadyBrush");
        RepairStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, IsBusy ? "WarningBrush" : "ReadyBrush");
        if (!active) StatusDetail.Text = UserStatusSub.Text = RepairStatusSub.Text = _repairRunning
            ? (_english ? "Windows repair active" : "Windows-Reparatur aktiv")
            : (_english ? "No action active" : "Keine Aktion aktiv");
        if (!active) UpdateRepairStatus();
    }

    private string TimerLabel() => TimerLabel(_seconds);
    private static string CountdownLabel(int seconds) => TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    private string TimerLabel(int seconds)
    {
        var minutes = Math.Max(0, (int)Math.Ceiling(seconds / 60.0));
        if (minutes >= 60)
            return _english ? $"{minutes / 60} hr {minutes % 60} min" : $"{minutes / 60} Std. {minutes % 60} Min.";
        return _english ? $"{minutes} minutes" : $"{minutes} Minuten";
    }

    private void UpdateTimer() { _seconds = Math.Clamp(_seconds, 60, 86400); TimerValue.Text = TimerLabel(); }
    private void TimerMinusMinute(object s, RoutedEventArgs e) { _seconds -= 60; UpdateTimer(); }
    private void TimerPlusMinute(object s, RoutedEventArgs e) { _seconds += 60; UpdateTimer(); }
    private void TimerMinusHour(object s, RoutedEventArgs e) { _seconds -= 3600; UpdateTimer(); }
    private void TimerPlusHour(object s, RoutedEventArgs e) { _seconds += 3600; UpdateTimer(); }

    private void SwitchPage(object s, RoutedEventArgs e)
    {
        _pageIndex = Math.Clamp(_pageIndex + (ReferenceEquals(s, PagePrevious) ? -1 : 1), 0, 2);
        HomePage.Visibility = _pageIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        UserPage.Visibility = _pageIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        RepairPage.Visibility = _pageIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePageNavigation();
        // Die Seite wechselt sofort; die Windows-Abfrage erfolgt nur auf Klick auf Aktualisieren.
    }

    private void UpdatePageNavigation()
    {
        PageIndicator.Text = $"{_pageIndex + 1} / 3";
        PagePrevious.IsEnabled = _pageIndex > 0;
        PageNext.IsEnabled = _pageIndex < 2;
    }

    internal void ShowRepairPage()
    {
        _pageIndex = 2;
        HomePage.Visibility = UserPage.Visibility = Visibility.Collapsed;
        RepairPage.Visibility = Visibility.Visible;
        UpdatePageNavigation();
    }

    private void ToggleSettings(object s, RoutedEventArgs e)
    {
        var visible = SettingsPanel.Visibility != Visibility.Visible;
        SettingsPanel.Visibility = SettingsBackdrop.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        HomePage.IsEnabled = UserPage.IsEnabled = RepairPage.IsEnabled = PageNavigation.IsEnabled = !visible;
    }
    private void Minimize(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseApp(object s, RoutedEventArgs e) => Close();
    private void DragWindow(object s, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void ClearLog(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show(_english ? "Do you want to permanently delete the event log?" : "Möchten Sie das Ereignisprotokoll unwiderruflich löschen?", _english ? "Delete event log?" : "Ereignisprotokoll löschen?", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { lock (_logLock) { if (File.Exists(LogFilePath)) File.Delete(LogFilePath); } }
        catch (Exception exception)
        {
            MessageBox.Show((_english ? "The log could not be cleared: " : "Das Protokoll konnte nicht gelöscht werden: ") + exception.Message, "Log PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LogBox.Items.Clear();
        UserLogBox.Items.Clear();
        Log(_english ? "Event log cleared." : "Ereignisprotokoll gelöscht.");
    }

    private void RefreshLog(object s, RoutedEventArgs e)
    {
        ReloadPersistedLog();
        Log(_english ? "Event log refreshed." : "Ereignisprotokoll aktualisiert.");
    }

    private void OpenLog(object s, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            if (!File.Exists(LogFilePath)) File.WriteAllText(LogFilePath, string.Empty);
            Process.Start(new ProcessStartInfo(LogFilePath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show((_english ? "The log file could not be opened: " : "Die Logdatei konnte nicht geöffnet werden: ") + exception.Message, "Log PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void German(object s, RoutedEventArgs e) { _english = false; ApplyLanguage(); LoadUsers(null, null!); SaveSettings(); Log("Sprache: Deutsch"); }
    private void English(object s, RoutedEventArgs e) { _english = true; ApplyLanguage(); LoadUsers(null, null!); SaveSettings(); Log("Language: English"); }

    private void ApplyLanguage()
    {
        TitleText.Text = "Power Control";
        UpdatePageNavigation();
        MottoText.Text = _english ? "FAST  /  SAFE  /  RELIABLE" : "SCHNELL  /  SICHER  /  ZUVERLÄSSIG";
        ImmediateTitle.Text = _english ? "Immediate" : "Sofort"; ImmediateSub.Text = _english ? "Direct system actions" : "Direkte Systemaktionen";
        ShutdownLabel.Text = TimerShutdownLabel.Text = _english ? "Shut down" : "Herunterfahren"; ShutdownSub.Text = _english ? "Turn off PC" : "PC ausschalten";
        RestartLabel.Text = TimerRestartLabel.Text = _english ? "Restart" : "Neustart"; RestartSub.Text = _english ? "Restart PC" : "PC neu starten";
        TimerLockLabel.Text = _english ? "Lock" : "Sperren"; TimerShutdownSub.Text = _english ? "Timer shutdown" : "Timer Herunterfahren"; TimerLockSub.Text = _english ? "Timer lock" : "Timer Sperren"; TimerRestartSub.Text = _english ? "Timer restart" : "Timer Neustart";
        LogoutLabel.Text = _english ? "Sign out" : "Abmelden"; LogoutSub.Text = _english ? "Sign out current user" : "Aktuellen Benutzer abmelden";
        LockLabel.Text = _english ? "Lock" : "Sperren"; LockSub.Text = _english ? "Lock Windows" : "Windows sperren";
        TimerTitle.Text = "Timer"; TimerSub.Text = _english ? "Automatic action after time" : "Automatische Aktion nach Zeit"; AbortLabel.Text = _english ? "Cancel timer" : "Timer abbrechen"; AbortHint.Text = _english ? "Stop the scheduled action" : "Geplante Aktion stoppen";
        TimerMinusMin.Content = _english ? "−1 min" : "−1 Min"; TimerPlusMin.Content = _english ? "+1 min" : "+1 Min"; TimerMinusHourButton.Content = _english ? "−1 hr" : "−1 Std"; TimerPlusHourButton.Content = _english ? "+1 hr" : "+1 Std";
        StatusTitle.Text = UserStatusTitle.Text = RepairStatusTitle.Text = "Status"; StatusSub.Text = UserStatusHeaderSub.Text = RepairStatusHeaderSub.Text = _english ? "System information" : "System-Informationen";
        if (_timer.IsEnabled)
        {
            SetTimerState(true);
            TimerTick(null, EventArgs.Empty);
        }
        else
        {
            SetTimerState(false);
        }
        LogSub.Text = UserLogHeaderSub.Text = _english ? "Event log" : "Ereignisprotokoll"; RefreshLogLabel.Text = _english ? "Refresh" : "Aktualisieren"; ClearLogLabel.Text = _english ? "Clear" : "Löschen"; OpenLogLabel.Text = _english ? "Open file" : "Datei öffnen";
        UsersTitle.Text = _english ? "User overview" : "Benutzerübersicht"; UsersSub.Text = _english ? "Local Windows users" : "Lokale Windows-Benutzer"; RefreshUsersButton.Content = _english ? "↻ Refresh" : "↻ Aktualisieren"; ResetPasswordButton.Content = _english ? "Reset password" : "Kennwort zurücksetzen";
        RepairTitle.Text = _english ? "Windows repair" : "Windows-Reparatur";
        RepairHeaderSub.Text = _english ? "Check and repair system files" : "Systemdateien prüfen und reparieren";
        RepairExplanation.Text = _english
            ? "Run DISM first, then SFC. Windows will not restart automatically."
            : "Zuerst DISM, danach SFC ausführen. Es wird nichts automatisch neu gestartet.";
        RepairDismSub.Text = _english ? "Repair Windows image" : "Windows-Abbild reparieren";
        RepairSfcSub.Text = _english ? "Check system files" : "Systemdateien prüfen";
        RepairCancelLabel.Text = _english ? "Cancel" : "Abbrechen";
        RepairLogTitle.Text = "Log PRO";
        RepairLogSub.Text = _english ? "Repair log" : "Reparaturprotokoll";
        OpenRepairLogLabel.Text = _english ? "Open file" : "Datei öffnen";
        ClearRepairLogLabel.Text = _english ? "Delete" : "Löschen";
        UpdateRepairProtocolButtonLabel();
        UpdateRepairTime();
        if (_repairRunning && _repairCommand is not null)
            RepairProgressLabel.Text = _repairPercent > 0 ? $"{_repairCommand}: {_repairPercent:0.#} %"
                : (_english ? $"{_repairCommand} is running…" : $"{_repairCommand} läuft…");
        else if (_lastRepairCancelled)
            RepairProgressLabel.Text = _english ? "Repair cancelled." : "Reparatur abgebrochen.";
        else if (_lastRepairExitCode is int repairCode)
        {
            _lastRepairSummary = RepairResultSummary.Create(_repairCommand?.StartsWith("SFC", StringComparison.Ordinal) == true ? "sfc" : "dism",
                repairCode, false, _repairOutput.ToString(), GetRepairFailureHint(null), _repairWatch.Elapsed, _english);
            RepairProgressLabel.Text = _lastRepairSummary.Title;
            RepairResult.Text = _lastRepairSummary.Message;
        }
        else RepairProgressLabel.Text = _english ? "Ready. Select a repair." : "Bereit. Reparatur auswählen.";
        UpdateRepairStatus();
        UserNameColumn.Header = _english ? "User" : "Benutzer"; UserRoleColumn.Header = _english ? "Type" : "Typ"; UserDescriptionColumn.Header = _english ? "Description" : "Beschreibung"; UserStateColumn.Header = _english ? "Status" : "Status";
        SettingsTitle.Text = _english ? "Options" : "Optionen"; SettingsSub.Text = _english ? "Adjust app settings" : "App-Einstellungen anpassen"; LanguageTitle.Text = _english ? "Language" : "Sprache"; ThemeTitle.Text = _english ? "Design" : "Design"; SizeTitle.Text = _english ? "App size" : "App-Größe";
        WarningTitle.Text = _english ? "Timer warning" : "Timer-Warnung"; WarningSound.Content = _english ? "Warning enabled" : "Warnung aktiv"; WarningSoundTitle.Text = _english ? "Warning tone" : "Warnton"; WarningVolumeTitle.Text = _english ? "Timer volume" : "Timer-Lautstärke"; TestSoundButton.Content = _english ? "Test" : "Test";
        AutoStartTitle.Text = _english ? "Start with Windows" : "Mit Windows starten"; AutoStart.Content = _english ? "Autostart enabled" : "Autostart aktiv"; DoneButton.Content = _english ? "Done" : "Fertig";
        SetComboContents(SizeBox, _english ? new[] { "Small · 860 × 620", "Standard · 900 × 650", "Large · 1050 × 760", "Very large · 1200 × 870" } : new[] { "Klein · 860 × 620", "Standard · 900 × 650", "Groß · 1050 × 760", "Sehr groß · 1200 × 870" });
        SetComboContents(WarningSoundBox, _english
            ? new[] { "Alarm", "Beep", "Chime", "Siren", "Double tone", "Triple tone", "Gong", "Signal", "Urgent", "Digital", "System alert", "System warning" }
            : new[] { "Alarm", "Piep", "Glockenspiel", "Sirene", "Doppelton", "Dreifachton", "Gong", "Signal", "Dringend", "Digital", "System-Alarm", "System-Warnung" });
        foreach (var item in ThemeBox.Items.OfType<ComboBoxItem>())
        {
            if (ThemeId(item) == "Graphit") item.Content = _english ? "Graphite" : "Graphit";
            if (ThemeId(item) == "Classic") item.Content = "Windows Classic";
        }
        TimerValue.Text = TimerLabel();
        UpdateCurrentUserLabel();
        if (IsLoaded) (System.Windows.Application.Current as App)?.UpdateTrayPresentation(SelectedTheme(), _english);
    }

    private static void SetComboContents(System.Windows.Controls.ComboBox box, IReadOnlyList<string> values)
    {
        for (var index = 0; index < Math.Min(box.Items.Count, values.Count); index++)
            if (box.Items[index] is ComboBoxItem item) item.Content = values[index];
    }

    private void LoadAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
        AutoStart.IsChecked = key?.GetValue(AutoStartKey) is not null;
    }

    private void ToggleAutoStart(object s, RoutedEventArgs e)
    {
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
        if (key is null) { MessageBox.Show(_english ? "Windows autostart could not be opened." : "Der Windows-Autostart konnte nicht geöffnet werden.", "Autostart", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (AutoStart.IsChecked == true)
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            key.SetValue(AutoStartKey, $"\"{path}\"");
            Log(_english ? "Windows autostart enabled." : "Windows-Autostart aktiviert.");
        }
        else { key.DeleteValue(AutoStartKey, false); Log(_english ? "Windows autostart disabled." : "Windows-Autostart deaktiviert."); }
    }

    private void ThemeChanged(object s, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ThemeBox.SelectedItem is not ComboBoxItem item) return;
        var name = ThemeId(item);
        ApplyTheme(name);
        SaveSettings();
        Log(_english ? $"Design changed: {name}" : $"Design geändert: {name}");
    }

    private void ApplyTheme(string name)
    {
        var palette = ThemePalette.For(name);
        var colors = palette.Colors;
        var keys = new[] { "AppBrush", "HeaderBrush", "FooterBrush", "CardBrush", "InputBrush", "TextBrush", "SubtleTextBrush", "AccentBrush", "InputBorderBrush", "ButtonBrush", "HoverBrush" };
        for (var index = 0; index < keys.Length; index++) Resources[keys[index]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index]));
        // Keep the dimensional card finish tied to the selected palette rather than
        // hard-coding the reference blue into every design.
        var card = (Color)ColorConverter.ConvertFromString(colors[3]);
        var button = (Color)ColorConverter.ConvertFromString(colors[9]);
        var input = (Color)ColorConverter.ConvertFromString(colors[4]);
        Resources["CardSurfaceBrush"] = new LinearGradientBrush(
            BlendColor(card, button, 0.36), BlendColor(card, input, 0.56), 45);
        Resources["HeaderTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.HeaderText));
        Resources["HeaderSubtleBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.HeaderSubtle));
        Resources["FooterTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.FooterText));
        var actionColors = palette.Actions;
        var actionKeys = new[] { "DangerBrush", "DangerBorderBrush", "SuccessBrush", "LockBrush", "CancelBrush", "WarningBrush" };
        for (var index = 0; index < actionKeys.Length; index++) Resources[actionKeys[index]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(actionColors[index]));
        Resources["ReadyBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Ready));
        Resources["CardBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.IsNeon ? colors[7] : colors[8]));
        Resources["ActionTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FBFF"));
        Resources["SelectionBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[10]));
        var glowHex = palette.Glow ?? (name == "Standard" ? colors[7] : null);
        var glowColor = glowHex is null ? Colors.Transparent : (Color)ColorConverter.ConvertFromString(glowHex);
        var glowOpacity = name == "Standard" ? 0.23 : palette.GlowOpacity;
        Resources["NeonGlow"] = new System.Windows.Media.Effects.DropShadowEffect { Color = glowColor, ShadowDepth = 0, BlurRadius = 15, Opacity = glowOpacity };
        Resources["NeonIconGlow"] = new System.Windows.Media.Effects.DropShadowEffect { Color = glowColor, ShadowDepth = 0, BlurRadius = 6, Opacity = glowOpacity * 0.7 };
        System.Windows.Documents.TextElement.SetFontFamily(Root, new System.Windows.Media.FontFamily(palette.FontFamily));
        ClearLogButton.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ActionTextBrush");
        var cardRadius = new CornerRadius(name == "Classic" ? 8 : 13);
        foreach (var border in FindVisualChildren<Border>(Root))
            if (ReferenceEquals(border.Style, Resources["Card"]) || ReferenceEquals(border.Style, Resources["CardGlow"])) border.CornerRadius = cardRadius;
        var showCornerDetails = name is not ("Classic" or "Linux Mint" or "Retro");
        foreach (var corner in FindVisualChildren<System.Windows.Shapes.Path>(Root))
            if (ReferenceEquals(corner.Style, Resources["CardCorner"]))
                corner.Visibility = showCornerDetails ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.CornerRadius = new CornerRadius(name == "Classic" ? 8 : 14);
        SettingsHalo.CornerRadius = SettingsPanel.CornerRadius;
        SwitchIconSet(palette.IconSet);
        (System.Windows.Application.Current as App)?.UpdateTrayPresentation(name, _english);
    }

    private static Color BlendColor(Color first, Color second, double amount) => Color.FromRgb(
        (byte)Math.Round(first.R + (second.R - first.R) * amount),
        (byte)Math.Round(first.G + (second.G - first.G) * amount),
        (byte)Math.Round(first.B + (second.B - first.B) * amount));

    private void SwitchIconSet(string set)
    {
        foreach (var image in FindVisualChildren<Image>(Root))
        {
            if (image.Name == nameof(AbortTimerIcon)) continue;
            var source = image.Source?.ToString();
            if (string.IsNullOrWhiteSpace(source) || !source.Contains("/icons/", StringComparison.OrdinalIgnoreCase)) continue;
            var file = Path.GetFileName(source);
            var resource = new Uri($"pack://application:,,,/Assets/icons/{set}/{file}", UriKind.Absolute);
            image.Source = new BitmapImage(resource);
            if (set == "neon" && image.Width >= 25)
                image.SetResourceReference(UIElement.EffectProperty, "NeonIconGlow");
            else image.ClearValue(UIElement.EffectProperty);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private void AppSizeChanged(object s, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SizeBox.SelectedIndex < 0) return;
        ApplySize();
        SaveSettings();
    }

    private void TestWarningSound(object s, RoutedEventArgs e)
    {
        if (!PlayWarningSound())
            MessageBox.Show(_english ? "The selected warning sound could not be played." : "Der gewählte Warnton konnte nicht abgespielt werden.", "Power Control PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool PlayWarningSound(string? overrideFile = null, double volumeFactor = 1)
    {
        var file = overrideFile ?? (WarningSoundBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system-warning.wav";
        if (!WarningSoundBox.Items.OfType<ComboBoxItem>().Any(item => item.Tag?.ToString() == file)) return false;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Power Control PRO", "sound-cache", file);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/sounds/warnings/{file}"))
                    ?? throw new FileNotFoundException(file);
                using (resource.Stream)
                using (var output = File.Create(path)) resource.Stream.CopyTo(output);
            }
            _warningPlayer?.Stop();
            _warningPlayer?.Close();
            _warningPlaybackFactor = volumeFactor;
            _warningPlayer = new MediaPlayer { Volume = WarningVolume.Value / 100 * volumeFactor };
            _warningPlayer.Open(new Uri(path, UriKind.Absolute));
            _warningPlayer.Play();
            return true;
        }
        catch (Exception exception)
        {
            Log((_english ? "Warning sound failed: " : "Warnton fehlgeschlagen: ") + exception.Message);
            return false;
        }
    }

    private async void LoadUsers(object? s, RoutedEventArgs e)
    {
        RefreshUsersButton.IsEnabled = false;
        try
        {
            var users = await Task.Run(ReadLocalUsers);
            UserList.Items.Clear();
            foreach (var user in users) UserList.Items.Add(user);
            UpdateCurrentUserLabel(users);
            Log(_english ? "User overview refreshed." : "Benutzerübersicht aktualisiert.");
        }
        catch (Exception exception)
        {
            Log((_english ? "User overview failed: " : "Benutzerübersicht fehlgeschlagen: ") + exception.Message);
            MessageBox.Show((_english ? "Local users could not be loaded: " : "Lokale Benutzer konnten nicht geladen werden: ") + exception.Message, "Power Control PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { RefreshUsersButton.IsEnabled = true; }
    }

    private void UpdateCurrentUserLabel(IReadOnlyList<UserEntry>? users = null)
    {
        CurrentUserText.Text = (_english ? "Current user: " : "Aktueller Benutzer: ") + Environment.UserName;
        var admin = users?.FirstOrDefault(user => user.Name.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))?.Role == "Administrator" || IsAdmin();
        CurrentUserRole.Text = admin ? "Administrator" : (_english ? "Standard user" : "Standardbenutzer");
        CurrentUserRole.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, admin ? "ReadyBrush" : "AccentBrush");
    }

    private void UserSelectionChanged(object s, SelectionChangedEventArgs e) => ResetPasswordButton.IsEnabled = UserList.SelectedItem is UserEntry;

    private void ResetPassword(object s, RoutedEventArgs e)
    {
        if (UserList.SelectedItem is not UserEntry user) return;
        var confirm = _english
            ? $"Reset the Windows password for {user.Name}? This will immediately replace the current password."
            : $"Windows-Kennwort für {user.Name} zurücksetzen? Das bisherige Kennwort wird sofort ersetzt.";
        if (MessageBox.Show(confirm, "Power Control PRO", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var password = PasswordResetDialog.Prompt(this, user.Name, _english);
        if (password is null) return;
        var info = new UserInfo1003 { Password = password };
        var result = NetUserSetInfo(null, user.Name, 1003, ref info, out _);
        info.Password = string.Empty;
        password = string.Empty;
        if (result == 0) { Log(_english ? $"Password changed for {user.Name}." : $"Kennwort für {user.Name} geändert."); MessageBox.Show(_english ? "The password was changed." : "Das Kennwort wurde geändert.", "Power Control PRO", MessageBoxButton.OK, MessageBoxImage.Information); }
        else
        {
            var detail = new Win32Exception(result).Message;
            if (result == 5) detail += _english ? " Start the app as administrator for another account." : " Für ein anderes Konto die App als Administrator starten.";
            Log((_english ? "Password change failed for " : "Kennwortänderung fehlgeschlagen für ") + user.Name + ": " + detail);
            MessageBox.Show((_english ? "Windows could not change the password: " : "Windows konnte das Kennwort nicht ändern: ") + detail, "Power Control PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private IReadOnlyList<UserEntry> ReadLocalUsers()
    {
        const string usersCommand = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Get-LocalUser | Select-Object Name,Enabled,Description | ConvertTo-Json -Compress";
        const string adminsCommand = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; Get-LocalGroupMember -SID S-1-5-32-544 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name | ConvertTo-Json -Compress";
        using var users = JsonDocument.Parse(RunCaptured("powershell.exe", $"-NoProfile -NonInteractive -Command \"{usersCommand}\""));
        var adminNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var admins = JsonDocument.Parse(RunCaptured("powershell.exe", $"-NoProfile -NonInteractive -Command \"{adminsCommand}\""));
            var names = admins.RootElement.ValueKind == JsonValueKind.Array ? admins.RootElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty) : new[] { admins.RootElement.GetString() ?? string.Empty };
            foreach (var name in names) adminNames.Add(name);
        }
        catch (Exception) { /* Keep the complete user list even if group membership cannot be read. */ }
        var entries = users.RootElement.ValueKind == JsonValueKind.Array ? users.RootElement.EnumerateArray().ToArray() : new[] { users.RootElement };
        return entries.Select(item =>
        {
            var name = item.GetProperty("Name").GetString() ?? "Unbekannt";
            var active = item.GetProperty("Enabled").GetBoolean();
            var description = item.TryGetProperty("Description", out var descriptionValue) && descriptionValue.ValueKind == JsonValueKind.String ? descriptionValue.GetString() ?? string.Empty : string.Empty;
            var admin = adminNames.Any(value => value.EndsWith("\\" + name, StringComparison.OrdinalIgnoreCase) || value.Equals(name, StringComparison.OrdinalIgnoreCase));
            return new UserEntry(name, admin ? "Administrator" : (_english ? "Local user" : "Lokales Benutzerkonto"), description, active ? (_english ? "Active" : "Aktiv") : (_english ? "Disabled" : "Deaktiviert"), active);
        }).OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string RunCaptured(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 }) ?? throw new InvalidOperationException();
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(5000)) { process.Kill(); throw new TimeoutException("Windows-Benutzerabfrage hat zu lange gedauert."); }
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException();
        return output;
    }

    private static bool IsAdmin() { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }

    public void ExecuteTrayAction(string action)
    {
        Dispatcher.Invoke(() =>
        {
            if (IsBusy)
            {
                MessageBox.Show(_english ? "Finish the active timer or repair first." : "Bitte zuerst den aktiven Timer oder die Reparatur beenden.", "Power Control PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            switch (action)
            {
                case "shutdown": ShutdownNow(this, new RoutedEventArgs()); break;
                case "restart": RestartNow(this, new RoutedEventArgs()); break;
                case "lock": LockNow(this, new RoutedEventArgs()); break;
            }
        });
    }
    private void Window_Closing(object? s, CancelEventArgs e) { if (!IsBusy) return; e.Cancel = true; MessageBox.Show(_english ? "Finish the active timer or repair first." : "Bitte zuerst den aktiven Timer oder die Reparatur beenden.", "Power Control PRO", MessageBoxButton.OK, MessageBoxImage.Warning); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct UserInfo1003 { [MarshalAs(UnmanagedType.LPWStr)] public string Password; }
    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetUserSetInfo(string? servername, string username, int level, ref UserInfo1003 buf, out int parmError);
}

public sealed record UserEntry(string Name, string Role, string Description, string Status, bool Enabled);

internal sealed class PasswordResetDialog : Window
{
    private readonly PasswordBox _first = new() { Margin = new Thickness(0, 5, 0, 10) };
    private readonly PasswordBox _second = new() { Margin = new Thickness(0, 5, 0, 12) };
    private readonly TextBlock _error = new() { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };
    private readonly bool _english;
    private string? Result { get; set; }

    private PasswordResetDialog(Window owner, string username, bool english)
    {
        _english = english; Owner = owner; Icon = owner.Icon; Title = english ? "Reset password" : "Kennwort zurücksetzen"; Width = 390; Height = 290; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var textBrush = owner.Resources["TextBrush"] as Brush ?? Brushes.White;
        var subtleBrush = owner.Resources["SubtleTextBrush"] as Brush ?? Brushes.LightSteelBlue;
        var cardBrush = owner.Resources["CardBrush"] as Brush ?? Brushes.Black;
        var buttonBrush = owner.Resources["ButtonBrush"] as Brush ?? Brushes.SteelBlue;
        var accentBrush = owner.Resources["AccentBrush"] as Brush ?? Brushes.SteelBlue;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = (english ? "New password for: " : "Neues Kennwort für: ") + username, FontWeight = FontWeights.Bold, Foreground = textBrush, FontSize = 16 });
        stack.Children.Add(new TextBlock { Text = english ? "Windows Hello PINs are managed in Windows and cannot be changed here." : "Windows-Hello-PINs werden von Windows verwaltet und können hier nicht geändert werden.", Foreground = subtleBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        stack.Children.Add(new TextBlock { Text = english ? "New password" : "Neues Kennwort", Foreground = textBrush });
        stack.Children.Add(_first);
        stack.Children.Add(new TextBlock { Text = english ? "Repeat password" : "Kennwort wiederholen", Foreground = textBrush });
        stack.Children.Add(_second);
        stack.Children.Add(_error);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var cancel = new Button { Content = english ? "Cancel" : "Abbrechen", Background = buttonBrush, Foreground = textBrush, BorderBrush = accentBrush, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(cancel);
        var confirm = new Button { Content = english ? "Change password" : "Kennwort ändern", Background = buttonBrush, Foreground = textBrush, BorderBrush = accentBrush, Padding = new Thickness(12, 5, 12, 5) };
        confirm.Click += Confirm;
        buttons.Children.Add(confirm);
        stack.Children.Add(buttons);
        Content = new Border { Padding = new Thickness(20), Background = cardBrush, BorderBrush = accentBrush, BorderThickness = new Thickness(2), Child = stack };
    }

    private void Confirm(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_first.Password)) { _error.Text = _english ? "Enter a password." : "Bitte ein Kennwort eingeben."; return; }
        if (_first.Password != _second.Password) { _error.Text = _english ? "The passwords do not match." : "Die Kennwörter stimmen nicht überein."; return; }
        Result = _first.Password; DialogResult = true;
    }

    public static string? Prompt(Window owner, string username, bool english) { var dialog = new PasswordResetDialog(owner, username, english); return dialog.ShowDialog() == true ? dialog.Result : null; }
}
