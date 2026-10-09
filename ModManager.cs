using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace EasyTurn;

public class ModManager
{
    /// <summary>Mod files the app always monitors. Also used to recognise mod files
    /// so the vanilla backup never copies one.</summary>
    public static readonly string[] DefaultMods = new[]
    {
        "ScriptHookV.dll",
        "ScriptHookVDotNet2.dll",
        "ScriptHookVDotNet3.dll",
        "AddonSpawner.asi",
        "combat_tweaks.asi",
        "DisableEditorWatermark.asi",
        "fwBoxStreamerVariable_DecalsLimit-Patch.asi",
        "HeapAdjuster.asi",
        "Menyoo.asi",
        "NoEditorRestrictions.asi",
        "openCameraV.asi",
        "OpenIV.asi",
        "PackfileLimitAdjuster.asi",
        "PoolManager.asi",
        "RDE_Auxiliary.asi",
        "ScriptHookVDotNet.asi",
        "Style control.asi",
        "WeaponLimitsAdjuster.asi",
        "Skin Control.asi",
        "TrainerV.asi",
        "dxgix.asi",
        "dinput8.dll"
    };

    public ObservableCollection<ModItem> Mods { get; } = new();
    private readonly AppSettings _settings;

    // Tracks the pre-Online state of non-core, non-excluded mods so we can restore them
    private readonly Dictionary<string, bool> _preOnlineState = new(StringComparer.OrdinalIgnoreCase);

    public ModManager(AppSettings settings)
    {
        _settings = settings;
    }

    public void LoadMods()
    {
        Mods.Clear();
        if (string.IsNullOrEmpty(_settings.GamePath) || !Directory.Exists(_settings.GamePath))
            return;

        // A game file must never stay disabled, and a banned mod must never come
        // back on — fix the disk state before deciding what to list.
        RestoreDisabledGameFiles();
        EnforceBannedMods();

        var banned = new HashSet<string>(_settings.BannedMods, StringComparer.OrdinalIgnoreCase);

        foreach (var modName in GetAllMonitoredNames())
        {
            // Banned mods disappear from the app's mod list entirely.
            if (banned.Contains(modName))
                continue;

            string enabledPath = Path.Combine(_settings.GamePath, modName);
            string disabledPath = ModItem.GetDisabledPath(enabledPath);

            if (File.Exists(enabledPath))
            {
                Mods.Add(new ModItem(modName, enabledPath, true));
            }
            else if (File.Exists(disabledPath))
            {
                Mods.Add(new ModItem(modName, disabledPath, false));
            }
        }
    }

    /// <summary>
    /// Every file the app monitors, in display order: the built-in mods followed by
    /// the user's own entries. GTA 5's own files are never included — they are not
    /// mods, must not be listed, and must never be toggled off.
    /// </summary>
    public IEnumerable<string> GetAllMonitoredNames() => DefaultMods
        .Concat(_settings.CustomMods)
        .Concat(_settings.OnlineExcludedMods)
        .Concat(_settings.BannedMods)
        .Where(name => !VanillaFiles.IsVanilla(name))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Restores any game file that was renamed to its disabled form (e.g. by an
    /// older version of the app, or by hand), so a game .dll can never be left off.
    /// </summary>
    public void RestoreDisabledGameFiles()
    {
        if (string.IsNullOrEmpty(_settings.GamePath) || !Directory.Exists(_settings.GamePath))
            return;

        foreach (string name in VanillaFiles.AllNames)
        {
            try
            {
                string enabledPath = Path.Combine(_settings.GamePath, name);
                string disabledPath = ModItem.GetDisabledPath(enabledPath);

                if (!File.Exists(enabledPath) && File.Exists(disabledPath))
                    File.Move(disabledPath, enabledPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to restore game file {name}: {ex.Message}");
            }
        }
    }

    /// <summary>Turns every banned mod off on disk (copy-safe: only ever renames).</summary>
    public void EnforceBannedMods()
    {
        if (string.IsNullOrEmpty(_settings.GamePath) || !Directory.Exists(_settings.GamePath))
            return;

        foreach (string modName in _settings.BannedMods)
        {
            if (VanillaFiles.IsVanilla(modName))
                continue;

            try
            {
                string enabledPath = Path.Combine(_settings.GamePath, modName);
                string disabledPath = ModItem.GetDisabledPath(enabledPath);

                if (!File.Exists(enabledPath))
                    continue;

                if (!File.Exists(disabledPath))
                {
                    File.Move(enabledPath, disabledPath);
                }
                else
                {
                    // Keep the banned copy and preserve the extra one instead of
                    // deleting it, so nothing is ever lost.
                    string backupPath = enabledPath + ".bak";
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(enabledPath, backupPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to ban {modName}: {ex.Message}");
            }
        }
    }

    public void SetOnlineReady(bool isOnline)
    {
        if (isOnline)
        {
            // Save current state of all mods before toggling
            _preOnlineState.Clear();
            foreach (var mod in Mods)
            {
                _preOnlineState[mod.Name] = mod.IsEnabled;
            }
        }

        foreach (var mod in Mods)
        {
            if (mod.Name.Equals("ScriptHookV.dll", StringComparison.OrdinalIgnoreCase) ||
                mod.Name.Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase))
            {
                // Core loaders: Disable for Online Ready, re-enable when going Offline
                mod.IsEnabled = !isOnline;
            }
            else if (_settings.OnlineExcludedMods.Any(excluded =>
                mod.Name.Equals(excluded, StringComparison.OrdinalIgnoreCase)))
            {
                // Online-excluded mods: Disable when going Online, re-enable when going Offline
                mod.IsEnabled = !isOnline;
            }
            else if (isOnline)
            {
                // Other mods: Enable when going Online
                mod.IsEnabled = true;
            }
            else
            {
                // Going Offline: restore the user's pre-Online state for this mod
                if (_preOnlineState.TryGetValue(mod.Name, out var previousState))
                {
                    mod.IsEnabled = previousState;
                }
            }
        }

        if (!isOnline)
        {
            _preOnlineState.Clear();
        }
    }

    /// <summary>
    /// Writes or removes args.txt in the game folder.
    /// When enabled, args.txt always contains the hardcoded BattlEye arguments
    /// ("-nobattleye -noBE"), followed by any extra arguments the user entered
    /// in the settings box.
    /// </summary>
    public void ApplyArguments(bool enabled)
    {
        if (string.IsNullOrEmpty(_settings.GamePath) || !Directory.Exists(_settings.GamePath))
            return;

        string argsFile = Path.Combine(_settings.GamePath, "args.txt");
        if (enabled)
        {
            var parts = new List<string>
            {
                // Hardcoded BattlEye arguments — always applied, not user-editable.
                AppSettings.BattlEyeArguments
            };

            // Append any user-supplied extra arguments (BattlEye tokens are stripped
            // centrally so they can never be duplicated or removed).
            string extras = AppSettings.SanitizeExtraArguments(_settings.GameArguments);
            if (!string.IsNullOrWhiteSpace(extras))
            {
                parts.AddRange(extras.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            File.WriteAllText(argsFile, string.Join(" ", parts));
        }
        else if (File.Exists(argsFile))
        {
            File.Delete(argsFile);
        }
    }

    /// <summary>
    /// Removes orphaned ".bak" backup files that ToggleFileOnDisk creates when it
    /// encounters a filename collision, so they don't accumulate in the game folder.
    /// A backup is only removed when the base name matches a monitored mod AND the
    /// file now at its original path is byte-identical to the backup — proving the
    /// backup holds no unique data. Otherwise the backup may be the only remaining
    /// copy of something and is preserved to avoid destroying data.
    /// </summary>
    public void CleanupBackups()
    {
        if (string.IsNullOrEmpty(_settings.GamePath) || !Directory.Exists(_settings.GamePath))
            return;

        // Build the set of base filenames this app manages so we never delete a
        // user's unrelated .bak file that happens to live in the game folder.
        var monitored = DefaultMods
            .Concat(_settings.CustomMods)
            .Concat(_settings.OnlineExcludedMods)
            .Concat(_settings.BannedMods)
            .Select(m => Path.GetFileNameWithoutExtension(m))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var bakFile in Directory.GetFiles(_settings.GamePath, "*.bak"))
            {
                string name = Path.GetFileName(bakFile);                    // e.g. Menyoo.asi.bak
                string withoutBak = Path.GetFileNameWithoutExtension(name); //      Menyoo.asi
                string baseName = Path.GetFileNameWithoutExtension(withoutBak); //  Menyoo

                if (!monitored.Contains(baseName))
                    continue;

                // The file this backup was copied from (e.g. Menyoo.asi). Only delete
                // the backup if it is byte-identical to that current file, so we never
                // discard content that only exists inside the backup.
                string original = Path.Combine(_settings.GamePath, withoutBak);
                if (File.Exists(original) && FilesAreIdentical(original, bakFile))
                {
                    File.Delete(bakFile);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to clean up backups: {ex.Message}");
        }
    }

    private static bool FilesAreIdentical(string first, string second)
    {
        try
        {
            var firstInfo = new FileInfo(first);
            var secondInfo = new FileInfo(second);
            if (firstInfo.Length != secondInfo.Length)
                return false;

            using var fs1 = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var fs2 = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int read;
            byte[] buffer1 = new byte[81920];
            byte[] buffer2 = new byte[81920];
            while ((read = fs1.Read(buffer1, 0, buffer1.Length)) > 0)
            {
                int read2 = fs2.Read(buffer2, 0, read);
                if (read2 != read)
                    return false;
                for (int i = 0; i < read; i++)
                {
                    if (buffer1[i] != buffer2[i])
                        return false;
                }
            }
            return true;
        }
        catch
        {
            // If we can't compare (e.g. file locked), keep the backup to be safe.
            return false;
        }
    }
}
