using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace EasyTurn;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ModManager _modManager;
    private readonly VanillaBackupManager _backupManager;
    private readonly ObservableCollection<OnlineExclusionEntry> _exclusionEntries = new();
    private readonly ObservableCollection<BannedModEntry> _bannedEntries = new();

    // True once the constructor finished wiring the UI, so control events (language
    // combo, appearance sliders, font combo) ignore the values they are given while
    // the window is still being built.
    private bool _uiReady;

    // True while the appearance controls are being re-synced from settings.
    private bool _updatingAppearance;

    // True while a copy operation runs in the background.
    private bool _busy;

    private string _statusKey = "ready";
    private object?[] _statusArgs = Array.Empty<object?>();

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _settings.LoadFromIni();
        _modManager = new ModManager(_settings);
        _backupManager = new VanillaBackupManager(_settings);

        Localization.Instance.Language = _settings.Language;
        FlowDirection = Localization.Instance.FlowDirection;

        ModItemsControl.ItemsSource = _modManager.Mods;
        BannedControl.ItemsSource = _bannedEntries;
        ExclusionsControl.ItemsSource = _exclusionEntries;
        BackupItemsControl.ItemsSource = _backupManager.Backups;

        CmbLanguage.ItemsSource = Localization.Languages;
        CmbLanguage.SelectedValuePath = "Code";
        CmbLanguage.DisplayMemberPath = "NativeName";
        CmbLanguage.SelectedValue = Localization.Instance.Language;

        if (!string.IsNullOrEmpty(_settings.GamePath))
        {
            TxtPath.Text = _settings.GamePath;
            _modManager.LoadMods();
        }

        InitArgsButton();
        InitOnlineReadyButton();
        _modManager.CleanupBackups();
        RefreshBackups();
        RebuildBannedList();
        RebuildExclusionList();

        SyncAppearanceControls();

        SetStatus("ready");
        _uiReady = true;
    }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        TxtStatus.Text = Localization.F(key, args);
    }

    // Re-renders the last status message in the newly selected language.
    private void RefreshStatusText() => TxtStatus.Text = Localization.F(_statusKey, _statusArgs);

    private void InitOnlineReadyButton()
    {
        BtnOnlineReady.IsChecked = _settings.Online;
        if (_settings.Online && !string.IsNullOrEmpty(_settings.GamePath))
        {
            // Give the bound ModItem.IsEnabled setters a chance to rename files,
            // keeping disk state in sync with the saved Online flag.
            _modManager.SetOnlineReady(true);
        }
        UpdateOnlineReadyButton();
    }

    private void UpdateOnlineReadyButton()
    {
        // The label describes the current mod state: ON = mods off, which means the
        // game is ready for online. The saturated/grey colours come from the style.
        bool isOnline = BtnOnlineReady.IsChecked ?? false;
        BtnOnlineReady.Content = Localization.T(isOnline ? "modsOff" : "modsOn");
    }

    private void InitArgsButton()
    {
        // Verify the actual on-disk state of args.txt instead of trusting the
        // saved setting, so manual edits to the file are reflected correctly.
        SyncArgsButtonWithDisk();
        UpdateArgsButton();
    }

    // Reads the actual args.txt presence on disk and syncs the button + setting.
    private void SyncArgsButtonWithDisk()
    {
        bool argsOnDisk = false;
        if (!string.IsNullOrEmpty(_settings.GamePath) && Directory.Exists(_settings.GamePath))
        {
            argsOnDisk = File.Exists(Path.Combine(_settings.GamePath, "args.txt"));
        }

        BtnArgs.IsChecked = argsOnDisk;
        _settings.BattlEyeOff = argsOnDisk;
    }

    private void UpdateArgsButton()
    {
        bool isOff = BtnArgs.IsChecked ?? false;
        BtnArgs.Content = Localization.T(isOff ? "battleyeOff" : "battleyeOn");
    }

    private void BtnArgs_Click(object sender, RoutedEventArgs e)
    {
        bool isOff = BtnArgs.IsChecked ?? false;
        _settings.BattlEyeOff = isOff;
        _settings.Save();
        _modManager.ApplyArguments(isOff);
        UpdateArgsButton();
        SetStatus(isOff ? "statusBattleEyeOff" : "statusBattleEyeOn");
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // MouseDown bubbles up from child controls too. Only drag the window when
        // the click originates on the window background, not on an interactive
        // control (buttons, checkboxes, text boxes, etc.) — otherwise every click
        // tries to DragMove and interferes with normal control interaction.
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (e.OriginalSource is DependencyObject source && IsWithinInteractiveControl(source))
            return;
        this.DragMove();
    }

    // Walks up the visual tree from source looking for an interactive control.
    private static bool IsWithinInteractiveControl(DependencyObject source)
    {
        System.Windows.Media.Visual? visual = source as System.Windows.Media.Visual;
        while (visual != null)
        {
            if (visual is System.Windows.Controls.Primitives.ButtonBase ||
                visual is System.Windows.Controls.TextBox ||
                visual is System.Windows.Controls.Primitives.ScrollBar ||
                visual is System.Windows.Controls.ComboBox)
            {
                return true;
            }
            visual = System.Windows.Media.VisualTreeHelper.GetParent(visual) as System.Windows.Media.Visual;
        }
        return false;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Localization.T("selectFolderTitle"),
            InitialDirectory = string.IsNullOrEmpty(_settings.GamePath) ? "C:\\" : _settings.GamePath
        };

        if (dialog.ShowDialog() == true)
        {
            string path = dialog.FolderName;
            if (File.Exists(Path.Combine(path, "GTA5.exe")))
            {
                _settings.GamePath = path;
                _settings.Save();
                TxtPath.Text = path;
                _modManager.LoadMods();
                // If Online Ready is already on, apply it to the newly loaded mods
                // so their on-disk state matches the active toggle (otherwise the
                // button would read "Online Ready" but no files would be renamed).
                if (_settings.Online)
                {
                    _modManager.SetOnlineReady(true);
                }
                // The new folder may have its own args.txt, so reflect its actual
                // on-disk state instead of leaving a stale label/setting.
                SyncArgsButtonWithDisk();
                UpdateArgsButton();
                RefreshBackups();
                RebuildBannedList();
                RebuildExclusionList();
                SetStatus("statusFolderUpdated");
            }
            else
            {
                MessageBox.Show(Localization.T("errInvalidFolder"),
                    Localization.T("errInvalidFolderTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnOnlineReady_Click(object sender, RoutedEventArgs e)
    {
        bool isOnline = BtnOnlineReady.IsChecked ?? false;
        _settings.Online = isOnline;
        _settings.Save();
        _modManager.SetOnlineReady(isOnline);
        UpdateOnlineReadyButton();
        SetStatus(isOnline ? "statusOnline" : "statusMods");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        RebuildBannedList();
        RebuildExclusionList();
        TxtCustomMods.Text = string.Join(Environment.NewLine, _settings.CustomMods);
        TxtGameArgs.Text = _settings.GameArguments;
        SyncAppearanceControls();
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    // Lists every file the app monitors so the user can ban one. Banned mods are
    // turned off for good and never come back through the main screen.
    private void RebuildBannedList()
    {
        _bannedEntries.Clear();

        var banned = new HashSet<string>(_settings.BannedMods, StringComparer.OrdinalIgnoreCase);
        foreach (var name in _modManager.GetAllMonitoredNames())
            _bannedEntries.Add(new BannedModEntry(name, banned.Contains(name)));
    }

    // Builds the exclusion checklist from the monitored files that are not banned
    // and are not game files, so files like xinput1_4.dll can be ticked before they
    // even exist in the game folder.
    private void RebuildExclusionList()
    {
        _exclusionEntries.Clear();

        var excluded = new HashSet<string>(_settings.OnlineExcludedMods, StringComparer.OrdinalIgnoreCase);
        var banned = new HashSet<string>(_settings.BannedMods, StringComparer.OrdinalIgnoreCase);

        foreach (var name in _modManager.GetAllMonitoredNames())
        {
            if (banned.Contains(name))
                continue;
            _exclusionEntries.Add(new OnlineExclusionEntry(name, excluded.Contains(name)));
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var customList = SplitLines(TxtCustomMods.Text);

        var onlineExcludedList = new List<string>();
        foreach (var entry in _exclusionEntries)
        {
            if (entry.IsExcluded && !onlineExcludedList.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                onlineExcludedList.Add(entry.Name);
        }

        var bannedList = new List<string>();
        foreach (var entry in _bannedEntries)
        {
            if (entry.IsBanned && !bannedList.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                bannedList.Add(entry.Name);
        }

        _settings.OnlineExcludedMods = onlineExcludedList;
        _settings.BannedMods = bannedList;
        _settings.CustomMods = customList;
        // Strip any BattlEye tokens so the hardcoded args can never be edited or
        // removed via the settings box — it only holds genuine extra arguments.
        _settings.GameArguments = AppSettings.SanitizeExtraArguments(TxtGameArgs.Text);
        _settings.Save();
        _modManager.LoadMods();
        // Re-apply args.txt so a changed/emptied GameArguments takes effect now.
        _modManager.ApplyArguments(_settings.BattlEyeOff);
        // A changed exclusion list has to take effect while Online Ready is already on.
        if (BtnOnlineReady.IsChecked == true)
            _modManager.SetOnlineReady(true);
        // Re-sync the args button label with the actual on-disk args.txt state.
        SyncArgsButtonWithDisk();
        UpdateArgsButton();
        RefreshBackups();

        SettingsOverlay.Visibility = Visibility.Collapsed;
        SetStatus("statusSettingsSaved");
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || CmbLanguage.SelectedValue is not string code)
            return;

        Localization.Instance.Language = code;
        FlowDirection = Localization.Instance.FlowDirection;

        _settings.Language = Localization.Instance.Language;
        _settings.Save();

        // State-dependent captions and the last status message are set in code.
        UpdateArgsButton();
        UpdateOnlineReadyButton();
        RefreshStatusText();
    }

    private static List<string> SplitLines(string text) => text
        .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
        .Select(s => s.Trim())
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();

    // ---- appearance -------------------------------------------------------------

    private const string DonateUrl = "https://bio.site/Sensin.mod";

    // Pushes the current appearance settings onto the controls.
    private void SyncAppearanceControls()
    {
        _updatingAppearance = true;
        try
        {
            SldOpacity.Value = Math.Clamp(_settings.BackgroundOpacity * 100, SldOpacity.Minimum, SldOpacity.Maximum);
            SldBlur.Value = Math.Clamp(_settings.BackgroundBlur, (int)SldBlur.Minimum, (int)SldBlur.Maximum);
        }
        finally
        {
            _updatingAppearance = false;
        }

        ApplyAppearance();
    }

    // Applies the text colour, background colour, transparency and blur to the window.
    private void ApplyAppearance()
    {
        Color color = ParseColor(_settings.BackgroundColor, Colors.Black);
        double opacity = Math.Clamp(_settings.BackgroundOpacity, 0, 1);

        RootBorder.Background = new SolidColorBrush(color) { Opacity = opacity };
        ColorSwatch.Background = new SolidColorBrush(color);
        TxtColorHex.Text = _settings.BackgroundColor.ToUpperInvariant();

        // Every label in the window binds to this resource, so replacing it
        // recolours all of the app's text at once.
        Color textColor = ParseColor(_settings.FontColor, Colors.White);
        Resources["TextColor"] = new SolidColorBrush(textColor);
        FontColorSwatch.Background = new SolidColorBrush(textColor);
        TxtFontColorHex.Text = _settings.FontColor.ToUpperInvariant();

        TxtOpacityValue.Text = ((int)Math.Round(opacity * 100)) + "%";
        TxtBlurValue.Text = _settings.BackgroundBlur.ToString();

        WindowEffects.ApplyBlur(this, _settings.BackgroundBlur, color, opacity);
    }

    private static Color ParseColor(string text, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(text) && ColorConverter.ConvertFromString(text) is Color color)
                return color;
        }
        catch (Exception)
        {
            // Fall through to the fallback colour.
        }
        return fallback;
    }

    private void Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _updatingAppearance)
            return;

        _settings.BackgroundOpacity = e.NewValue / 100.0;
        _settings.Save();
        ApplyAppearance();
    }

    private void Blur_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady || _updatingAppearance)
            return;

        _settings.BackgroundBlur = (int)Math.Round(e.NewValue);
        _settings.Save();
        ApplyAppearance();
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        Color? picked = PickColor(ParseColor(_settings.BackgroundColor, Colors.Black), _settings.BackgroundOpacity, out double alpha);
        if (picked is not Color color)
            return;

        // The picker also returns the alpha, which is the window transparency.
        _settings.BackgroundColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _settings.BackgroundOpacity = Math.Clamp(alpha, 0, 1);
        _settings.Save();
        SyncAppearanceControls();
    }

    private void PickFontColor_Click(object sender, RoutedEventArgs e)
    {
        Color? picked = PickColor(ParseColor(_settings.FontColor, Colors.White), 1.0, out _);
        if (picked is not Color color)
            return;

        _settings.FontColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _settings.Save();
        ApplyAppearance();
    }

    // Opens the Photoshop-style picker and returns the chosen colour (and its alpha).
    private Color? PickColor(Color initial, double alpha, out double pickedAlpha)
    {
        var dialog = new ColorPickerDialog(initial, alpha)
        {
            Owner = this,
            FontFamily = FontFamily,
        };

        if (dialog.ShowDialog() == true && dialog.Result is Color color)
        {
            pickedAlpha = dialog.ResultAlpha;
            return color;
        }

        pickedAlpha = alpha;
        return null;
    }

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _settings.FontColor = AppSettings.DefaultFontColor;
        _settings.BackgroundColor = AppSettings.DefaultBackgroundColor;
        _settings.BackgroundOpacity = AppSettings.DefaultBackgroundOpacity;
        _settings.BackgroundBlur = AppSettings.DefaultBackgroundBlur;
        _settings.Save();
        SyncAppearanceControls();
    }

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = DonateUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open the donate link: {ex.Message}");
        }
    }

    private void RefreshBackups()
    {
        _backupManager.Refresh();
        TxtNoBackups.Visibility = _backupManager.Backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = UpdateBackupFolderSizesAsync();
    }

    // Folder sizes can be large, so they are measured in the background and each
    // entry shows its size as soon as it is known.
    private async Task UpdateBackupFolderSizesAsync()
    {
        var entries = _backupManager.Backups.ToList();
        if (entries.Count == 0)
            return;

        var sizes = await Task.Run(() => entries
            .ToDictionary(entry => entry, entry => VanillaBackupManager.GetFolderSize(entry.FolderPath)));

        foreach (var pair in sizes)
            pair.Key.SizeBytes = pair.Value;
    }

    private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string folderPath } || !Directory.Exists(folderPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = folderPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open '{folderPath}': {ex.Message}");
        }
    }

    // Copies the current vanilla game files (never mod files) into a new backup
    // folder named after the installed game version. Copy only — nothing is moved
    // or deleted, and an existing folder for the same version needs confirmation.
    private async void BackupVanilla_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !EnsureGameReady())
            return;

        var vanillaFiles = _backupManager.GetVanillaFiles();
        if (vanillaFiles.Count == 0)
        {
            MessageBox.Show(Localization.T("errNoVanillaFiles"), Localization.T("infoTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string version = _backupManager.GetGameVersion();
        if (string.IsNullOrWhiteSpace(version))
            version = VanillaBackupManager.UnknownVersion;

        string folderPath = _backupManager.GetBackupFolderPath(version);
        if (Directory.Exists(folderPath))
        {
            var overwrite = MessageBox.Show(Localization.F("confirmOverwrite", folderPath),
                Localization.T("infoTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
                return;
        }

        string? error = await RunBusyAsync(() => _backupManager.CreateBackup(folderPath, vanillaFiles));
        if (error != null)
        {
            ShowCopyError(folderPath, error);
            return;
        }

        RefreshBackups();
        SetStatus("statusBackupDone", _backupManager.GetBackupFolderName(version));
    }

    private async void CopyFilesToGame_Click(object sender, RoutedEventArgs e) => await CopyBackupIntoGameAsync(
        sender, "confirmCopyTitle", "confirmCopy1", "confirmCopy2", "statusCopied");

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e) => await CopyBackupIntoGameAsync(
        sender, "confirmRestoreTitle", "confirmRestore1", "confirmRestore2", "statusRestored");

    // Both actions do the same thing (copy the backed-up files into the game folder,
    // overwriting what is there) and both ask for confirmation twice.
    private async Task CopyBackupIntoGameAsync(
        object sender, string titleKey, string confirmKey, string againKey, string doneKey)
    {
        if (_busy || !EnsureGameReady())
            return;

        if (sender is not Button { Tag: string folderPath })
            return;

        if (!Directory.Exists(folderPath) || !_backupManager.IsInsideBackupRoot(folderPath))
            return;

        var first = MessageBox.Show(Localization.F(confirmKey, folderPath), Localization.T(titleKey),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (first != MessageBoxResult.Yes)
            return;

        var second = MessageBox.Show(Localization.T(againKey), Localization.T(titleKey),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (second != MessageBoxResult.Yes)
            return;

        string? error = await RunBusyAsync(() => _backupManager.CopyFilesToGame(folderPath));
        if (error != null)
        {
            ShowCopyError(folderPath, error);
            return;
        }

        SetStatus(doneKey);
    }

    private bool EnsureGameReady()
    {
        if (!_backupManager.HasGameFolder)
        {
            MessageBox.Show(Localization.T("errNoGamePath"), Localization.T("infoTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (VanillaBackupManager.IsGameRunning())
        {
            MessageBox.Show(Localization.T("errGameRunning"), Localization.T("errGameRunningTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private async Task<string?> RunBusyAsync(Action action)
    {
        _busy = true;
        BtnBackupVanilla.IsEnabled = false;
        SetStatus("statusWorking");

        try
        {
            await Task.Run(action);
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Copy operation failed: {ex.Message}");
            return ex.Message;
        }
        finally
        {
            _busy = false;
            BtnBackupVanilla.IsEnabled = true;
        }
    }

    private void ShowCopyError(string folderPath, string error)
    {
        MessageBox.Show(Localization.F("errCopyFailed", folderPath, error), Localization.T("infoTitle"),
            MessageBoxButton.OK, MessageBoxImage.Error);
        SetStatus("ready");
    }

    private void CancelSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _settings.Save();
        this.Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
        {
            this.WindowState = WindowState.Normal;
            BtnMaximize.Content = "▢";
        }
        else
        {
            this.WindowState = WindowState.Maximized;
            BtnMaximize.Content = "❐";
        }
    }
}