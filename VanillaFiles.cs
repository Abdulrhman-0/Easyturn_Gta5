using System.IO;

namespace EasyTurn;

/// <summary>
/// The exact set of files GTA 5 ships in its root folder. Names only — taken from
/// the "!Gta original files Original Files" reference folder.
///
/// This list has two jobs:
///  * the vanilla backup copies exactly these files (and nothing else), so a mod
///    file can never end up inside a backup;
///  * these names are never treated as mods, so they are not listed in the app and
///    can never be toggled off — renaming a game .dll breaks the game.
/// </summary>
public static class VanillaFiles
{
    /// <summary>Vanilla file names, exactly as the game ships them.</summary>
    public static readonly string[] Names =
    {
        "bink2w64.dll",
        "common.rpf",
        "d3dcompiler_46.dll",
        "d3dcsx_46.dll",
        "EOSSDK-Win64-Shipping.dll",
        "EOSSDK-Win64-Shipping-1.17.1.3.dll",
        "fvad.dll",
        "GFSDK_ShadowLib.win64.dll",
        "GFSDK_TXAA.win64.dll",
        "GFSDK_TXAA_AlphaResolve.win64.dll",
        "GPUPerfAPIDX11-x64.dll",
        "GTA5.exe",
        "GTA5_BE.exe",
        "libcurl.dll",
        "libtox.dll",
        "NvPmApi.Core.win64.dll",
        "opus.dll",
        "opusenc.dll",
        "PlayGTAV.exe",
        "RedistributableUninstaller.exe",
        "title.rgl",
        "version.txt",
        "versioninfo.txt",
        "XCurl.dll",
        "zlib1.dll",
    };

    // Vanilla libraries whose file name carries a version number, so an update can
    // change the name (e.g. EOSSDK-Win64-Shipping-1.18.0.0.dll).
    private static readonly string[] VersionedNamePrefixes = { "EOSSDK-Win64-Shipping-" };

    // Optional external list: a "vanilla_files.txt" (or a reference folder) placed
    // next to EasyTurn.exe extends the built-in list without a rebuild.
    private const string ExternalListFileName = "vanilla_files.txt";
    private const string ReferenceFolderName = "!Gta original files Original Files";
    private const string ReferenceFolderAltName = "VanillaFiles";

    private static readonly HashSet<string> FileNames = BuildNames();
    private static readonly HashSet<string> BaseNames = FileNames
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => !string.IsNullOrEmpty(name))
        .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

    /// <summary>Every vanilla file name, including any added by the external list.</summary>
    public static IEnumerable<string> AllNames => FileNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the file name is one the game itself uses.</summary>
    public static bool IsVanillaFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string name = Path.GetFileName(fileName.Trim());
        return FileNames.Contains(name) || MatchesVersionedPrefix(name);
    }

    /// <summary>
    /// True when the file (given as a plain name or a full path, with or without an
    /// extension) belongs to the game and must never be shown or toggled.
    /// </summary>
    public static bool IsVanilla(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        if (IsVanillaFileName(fileName))
            return true;

        string baseName = Path.GetFileNameWithoutExtension(fileName.Trim());
        return !string.IsNullOrEmpty(baseName) && BaseNames.Contains(baseName);
    }

    /// <summary>The game files that actually exist in the given folder, as full paths.</summary>
    public static List<string> GetPresent(string gameFolder)
    {
        var present = new List<string>();
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            return present;

        foreach (string name in AllNames)
        {
            string path = Path.Combine(gameFolder, name);
            if (File.Exists(path))
                present.Add(path);
        }
        return present;
    }

    private static bool MatchesVersionedPrefix(string fileName)
    {
        foreach (string prefix in VersionedNamePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static HashSet<string> BuildNames()
    {
        var names = new HashSet<string>(Names, StringComparer.OrdinalIgnoreCase);

        foreach (string name in ReadExternalNames())
            names.Add(name);

        return names;
    }

    // Keeps the list maintainable without a rebuild: reads file names from a
    // "vanilla_files.txt" and/or a reference folder sitting next to the app.
    private static IEnumerable<string> ReadExternalNames()
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        foreach (string path in new[]
                 {
                     Path.Combine(baseDirectory, ExternalListFileName),
                     Path.Combine(baseDirectory, ReferenceFolderAltName, ExternalListFileName),
                 })
        {
            foreach (string name in ReadListFile(path))
                yield return name;
        }

        foreach (string folder in new[]
                 {
                     Path.Combine(baseDirectory, ReferenceFolderAltName),
                     Path.Combine(baseDirectory, ReferenceFolderName),
                 })
        {
            foreach (string name in ReadFolderNames(folder))
                yield return name;
        }
    }

    private static IEnumerable<string> ReadListFile(string path)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path))
                yield break;
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read '{path}': {ex.Message}");
            yield break;
        }

        foreach (string line in lines)
        {
            string name = line.Trim();
            if (name.Length > 0 && !name.StartsWith(';') && !name.StartsWith('#'))
                yield return name;
        }
    }

    private static IEnumerable<string> ReadFolderNames(string folder)
    {
        IEnumerable<string> files;
        try
        {
            if (!Directory.Exists(folder))
                yield break;
            files = Directory.GetFiles(folder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to list '{folder}': {ex.Message}");
            yield break;
        }

        foreach (string file in files)
            yield return Path.GetFileName(file);
    }
}
