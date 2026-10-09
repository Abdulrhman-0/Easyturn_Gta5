using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace EasyTurn;

/// <summary>One vanilla backup folder shown in the right-hand panel.</summary>
public sealed class VanillaBackupEntry : INotifyPropertyChanged
{
    public string FolderPath { get; init; } = string.Empty;
    public string FolderName { get; init; } = string.Empty;

    /// <summary>Game version parsed from the folder name, or null when it has none.</summary>
    public string? Version { get; init; }

    public string Title => string.IsNullOrEmpty(Version) ? FolderName : Version;

    private long _sizeBytes = -1;

    /// <summary>Total size of the folder in bytes, or -1 while it has not been measured.</summary>
    public long SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (_sizeBytes == value) return;
            _sizeBytes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeBytes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        }
    }

    /// <summary>Human readable folder size; empty until the size has been measured.</summary>
    public string SizeText => _sizeBytes < 0 ? string.Empty : VanillaBackupManager.FormatSize(_sizeBytes);

    private bool _isNewest;
    public bool IsNewest
    {
        get => _isNewest;
        set
        {
            if (_isNewest == value) return;
            _isNewest = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNewest)));
        }
    }

    private bool _isCurrentUsed;
    public bool IsCurrentUsed
    {
        get => _isCurrentUsed;
        set
        {
            if (_isCurrentUsed == value) return;
            _isCurrentUsed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrentUsed)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Creates and lists "vanilla files" backups of the GTA 5 folder. Only clean game
/// files (root .exe/.dll and common.rpf) are ever copied — never mod files — and
/// the app never moves or deletes anything: every operation is a plain copy.
/// </summary>
public class VanillaBackupManager
{
    /// <summary>Folder name layout requested by the user: prefix + game version.</summary>
    public const string BackupFolderPrefix = "Gta_V_vanilla files backup_Version_";

    /// <summary>Used in the folder name when the game version cannot be read.</summary>
    public const string UnknownVersion = "unknown";

    private const string VersionMarker = "_version_";

    public string RootDirectory { get; }

    public ObservableCollection<VanillaBackupEntry> Backups { get; } = new();

    private readonly AppSettings _settings;

    public VanillaBackupManager(AppSettings settings)
    {
        _settings = settings;
        RootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VanillaBackups");
    }

    public bool HasGameFolder =>
        !string.IsNullOrWhiteSpace(_settings.GamePath) && Directory.Exists(_settings.GamePath);

    /// <summary>True while GTA 5 is running, in which case files must not be touched.</summary>
    public static bool IsGameRunning()
    {
        try
        {
            return Process.GetProcessesByName("GTA5").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Version of the installed GTA5.exe (e.g. "1.0.3411.0"), or an empty string.</summary>
    public string GetGameVersion()
    {
        if (!HasGameFolder)
            return string.Empty;

        string executable = Path.Combine(_settings.GamePath, "GTA5.exe");
        if (!File.Exists(executable))
            return string.Empty;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executable);
            string version = info.FileVersion ?? string.Empty;
            if (string.IsNullOrWhiteSpace(version))
                version = info.ProductVersion ?? string.Empty;
            if (string.IsNullOrWhiteSpace(version))
                return string.Empty;

            // Some builds report "1.0.3411.0 (something)"; keep the numeric part only.
            version = version.Trim();
            int space = version.IndexOf(' ');
            if (space > 0)
                version = version.Substring(0, space);

            return version.Trim();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to read game version: {ex.Message}");
            return string.Empty;
        }
    }

    public string GetBackupFolderName(string version) => BackupFolderPrefix + version;

    public string GetBackupFolderPath(string version) => Path.Combine(RootDirectory, GetBackupFolderName(version));

    /// <summary>
    /// Every vanilla file the game uses from its root folder. The exact file names
    /// come from <see cref="VanillaFiles"/>, so a mod file can never be copied.
    /// </summary>
    public List<string> GetVanillaFiles() => VanillaFiles.GetPresent(_settings.GamePath);

    /// <summary>
    /// Copies the given files into the backup folder. Only ever copies — and the
    /// folder is first cleaned with <see cref="CleanBackupFolder"/> so a backup can
    /// never hold a mod file (for example one made by an older version of the app).
    /// </summary>
    public int CreateBackup(string folderPath, IEnumerable<string> files)
    {
        Directory.CreateDirectory(folderPath);
        CleanBackupFolder(folderPath);

        int copied = 0;
        foreach (string file in files)
        {
            string destination = Path.Combine(folderPath, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
            copied++;
        }
        return copied;
    }

    /// <summary>Subfolder stray files are parked in, never deleted.</summary>
    public const string QuarantineFolderName = "NotVanilla_Removed";

    /// <summary>
    /// Moves any file that is not a vanilla game file out of the backup folder and
    /// into a "NotVanilla_Removed" subfolder. Copies of the backup only take files
    /// from the top level, so a stray mod can never be pushed back into the game,
    /// and nothing is ever deleted.
    /// </summary>
    public void CleanBackupFolder(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return;

            foreach (string file in Directory.GetFiles(folderPath))
            {
                if (VanillaFiles.IsVanillaFileName(Path.GetFileName(file)))
                    continue;

                string quarantine = Path.Combine(folderPath, QuarantineFolderName);
                Directory.CreateDirectory(quarantine);

                // Keep every stray file: never overwrite an earlier quarantined copy.
                string name = Path.GetFileName(file);
                string target = Path.Combine(quarantine, name);
                for (int index = 1; File.Exists(target); index++)
                {
                    target = Path.Combine(quarantine,
                        $"{Path.GetFileNameWithoutExtension(name)}_{index}{Path.GetExtension(name)}");
                }

                File.Move(file, target);
                Debug.WriteLine($"Backup folder held a non-vanilla file; moved {name} to {QuarantineFolderName}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to clean the backup folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies every file of a backup folder back into the game folder, overwriting
    /// what is there. Used by both "Copy files to game folder" and "Restore".
    /// </summary>
    public int CopyFilesToGame(string backupFolderPath)
    {
        if (!HasGameFolder)
            throw new InvalidOperationException("No game folder selected.");

        int copied = 0;
        foreach (string file in Directory.GetFiles(backupFolderPath))
        {
            string destination = Path.Combine(_settings.GamePath, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
            copied++;
        }
        return copied;
    }

    /// <summary>Rebuilds the backup list, ordering by version and tagging the badges.</summary>
    public void Refresh()
    {
        string currentVersion = GetGameVersion();
        var entries = new List<VanillaBackupEntry>();

        try
        {
            if (Directory.Exists(RootDirectory))
            {
                foreach (string directory in Directory.GetDirectories(RootDirectory))
                {
                    string name = Path.GetFileName(directory);
                    entries.Add(new VanillaBackupEntry
                    {
                        FolderPath = directory,
                        FolderName = name,
                        Version = ParseVersion(name)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to list backups: {ex.Message}");
        }

        // Highest version first; folders without a version go last.
        entries.Sort((a, b) =>
        {
            int order = CompareVersions(b.Version, a.Version);
            return order != 0
                ? order
                : string.Compare(a.FolderName, b.FolderName, StringComparison.OrdinalIgnoreCase);
        });

        string? newestVersion = entries
            .Where(e => !string.IsNullOrEmpty(e.Version))
            .Select(e => e.Version)
            .FirstOrDefault();

        foreach (var entry in entries)
        {
            entry.IsNewest = !string.IsNullOrEmpty(newestVersion) &&
                             CompareVersions(entry.Version, newestVersion) == 0;
            entry.IsCurrentUsed = !string.IsNullOrEmpty(currentVersion) &&
                                  CompareVersions(entry.Version, currentVersion) == 0;
        }

        Backups.Clear();
        foreach (var entry in entries)
            Backups.Add(entry);
    }

    /// <summary>Guards against acting on a path outside the backup root.</summary>
    public bool IsInsideBackupRoot(string path)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Reads the version out of a backup folder name, if it has one.</summary>
    public static string? ParseVersion(string folderName)
    {
        int index = folderName.LastIndexOf(VersionMarker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;

        string version = folderName.Substring(index + VersionMarker.Length).Trim();
        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    /// <summary>Numeric-aware version comparison; -1/0/1, unversioned values sort lowest.</summary>
    public static int CompareVersions(string? first, string? second)
    {
        if (string.IsNullOrEmpty(first) && string.IsNullOrEmpty(second)) return 0;
        if (string.IsNullOrEmpty(first)) return -1;
        if (string.IsNullOrEmpty(second)) return 1;

        string[] left = first.Split('.');
        string[] right = second.Split('.');
        int count = Math.Max(left.Length, right.Length);

        for (int i = 0; i < count; i++)
        {
            int leftValue = i < left.Length && int.TryParse(left[i], out int lb) ? lb : 0;
            int rightValue = i < right.Length && int.TryParse(right[i], out int rb) ? rb : 0;
            if (leftValue != rightValue)
                return leftValue.CompareTo(rightValue);
        }
        return 0;
    }

    /// <summary>Total size of every file inside a folder, or -1 when it cannot be read.</summary>
    public static long GetFolderSize(string folderPath)
    {
        try
        {
            long total = 0;
            foreach (var file in new DirectoryInfo(folderPath).EnumerateFiles("*", SearchOption.AllDirectories))
                total += file.Length;
            return total;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to measure '{folderPath}': {ex.Message}");
            return -1;
        }
    }

    /// <summary>Formats a byte count for display (e.g. "1.94 GB").</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";

        double kilobytes = bytes / 1024.0;
        if (kilobytes < 1024)
            return kilobytes.ToString("0.#", CultureInfo.InvariantCulture) + " KB";

        double megabytes = kilobytes / 1024.0;
        if (megabytes < 1024)
            return megabytes.ToString("0.#", CultureInfo.InvariantCulture) + " MB";

        return (megabytes / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
    }
}
