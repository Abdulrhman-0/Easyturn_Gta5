using System.IO;
using System.Text.Json;

namespace EasyTurn;

public class AppSettings
{
    // Hardcoded BattlEye arguments that are ALWAYS applied when args.txt is written.
    // These are owned by the app and must never be editable or removable via the UI.
    public const string BattlEyeArguments = "-nobattleye -noBE";

    // Tokens that belong to the hardcoded BattlEye arguments. Any occurrence in
    // user-supplied extra arguments is stripped so they can never be duplicated
    // or disabled by editing the box.
    private static readonly string[] BattlEyeTokens = { "-nobattleye", "-noBE" };

    /// <summary>
    /// Removes the hardcoded BattlEye tokens from a user-supplied argument string.
    /// The box therefore only ever holds genuine extra arguments.
    /// </summary>
    public static string SanitizeExtraArguments(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return string.Empty;

        var tokens = new HashSet<string>(BattlEyeTokens, StringComparer.OrdinalIgnoreCase);
        return string.Join(" ", args
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => !tokens.Contains(a)));
    }

    public string GamePath { get; set; } = string.Empty;
    public List<string> CustomMods { get; set; } = new();
    public List<string> OnlineExcludedMods { get; set; } = new();
    // Mods the user banned in the settings panel: always off, never listed again.
    public List<string> BannedMods { get; set; } = new();
    public bool BattlEyeOff { get; set; } = false;
    // Extra launch arguments supplied by the user (BattlEye args are hardcoded).
    public string GameArguments { get; set; } = string.Empty;
    public bool Online { get; set; } = false;
    // UI language code (see Localization.Languages).
    public string Language { get; set; } = Localization.DefaultLanguage;

    // --- Appearance ---
    public const string DefaultFontColor = "#FFFFFF";
    public const string DefaultBackgroundColor = "#0A0A0A";
    public const double DefaultBackgroundOpacity = 0.5;
    public const int DefaultBackgroundBlur = 0;

    // Text colour used for the app's labels. 0% transparency is fully see-through.
    public string FontColor { get; set; } = DefaultFontColor;
    public string BackgroundColor { get; set; } = DefaultBackgroundColor;
    public double BackgroundOpacity { get; set; } = DefaultBackgroundOpacity;
    public int BackgroundBlur { get; set; } = DefaultBackgroundBlur;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EasyTurn",
        "settings.json"
    );

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                // Guard against a valid-but-empty payload returning null.
                if (loaded != null)
                {
                    // Never allow the hardcoded BattlEye args to be stored as editable
                    // extras (e.g. leftover from older versions).
                    string sanitizedArgs = SanitizeExtraArguments(loaded.GameArguments);
                    // A settings file written before the language feature, or one with
                    // an unknown code, must fall back to a supported language.
                    string language = Localization.Normalize(loaded.Language);
                    if (!string.Equals(sanitizedArgs, loaded.GameArguments, StringComparison.Ordinal) ||
                        !string.Equals(language, loaded.Language, StringComparison.Ordinal))
                    {
                        loaded.GameArguments = sanitizedArgs;
                        loaded.Language = language;
                        loaded.Save();
                    }
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // The settings file is corrupted (e.g. a partial write). Do NOT silently
            // discard it — preserve a copy on disk so no data is lost, then fall back
            // to a fresh instance so the app can still start.
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string backupPath = SettingsPath + ".bak";
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Copy(SettingsPath, backupPath);
                }
            }
            catch (Exception backupEx)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to back up corrupt settings: {backupEx.Message}");
            }
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public void LoadFromIni()
    {
        try
        {
            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mods.ini");
            if (File.Exists(iniPath))
            {
                var lines = File.ReadAllLines(iniPath)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith(";") && !l.StartsWith("#"));
                
                foreach (var line in lines)
                {
                    if (!CustomMods.Contains(line, StringComparer.OrdinalIgnoreCase))
                        CustomMods.Add(line);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load mods.ini: {ex.Message}");
        }
    }
}
