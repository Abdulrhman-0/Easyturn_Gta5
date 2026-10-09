using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace EasyTurn;

public class ModItem : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _fullPath;
    private string _name;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string FullPath
    {
        get => _fullPath;
        set { _fullPath = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                bool previousState = _isEnabled;
                _isEnabled = value;
                OnPropertyChanged();

                // If the on-disk rename fails (e.g. file locked because the game
                // is running), revert the in-memory state so the UI does not
                // desync from what is actually on disk.
                if (!ToggleFileOnDisk())
                {
                    _isEnabled = previousState;
                    OnPropertyChanged();
                }
            }
        }
    }

    public ModItem(string name, string fullPath, bool isEnabled)
    {
        _name = name;
        _fullPath = fullPath;
        _isEnabled = isEnabled;
    }

    /// <summary>
    /// The disabled (".x…") path for the given mod file path. Given an already
    /// disabled path the same path is returned.
    /// </summary>
    public static string GetDisabledPath(string filePath)
    {
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);
        string disabledExtension = extension.StartsWith(".x", StringComparison.OrdinalIgnoreCase)
            ? extension
            : ".x" + extension.Substring(1);
        return Path.Combine(directory, name + disabledExtension);
    }

    /// <summary>
    /// Renames the mod file on disk between the enabled and disabled states.
    /// Returns true when the disk state now matches <see cref="IsEnabled"/>,
    /// or false if the rename could not be performed (and the caller should
    /// revert the in-memory state).
    /// </summary>
    private bool ToggleFileOnDisk()
    {
        try
        {
            if (string.IsNullOrEmpty(FullPath) || !File.Exists(FullPath)) return true;

            string directory = Path.GetDirectoryName(FullPath)!;
            string fileName = Path.GetFileNameWithoutExtension(FullPath);
            string extension = Path.GetExtension(FullPath);

            // Safely compute the new extension.
            //   Disabling (.dll  → .xdll,  .asi  → .xasi): prepend 'x' after the dot
            //   Enabling  (.xdll → .dll,   .xasi → .asi):  remove the leading ".x"
            // Only strip the ".x" prefix when the current extension actually starts
            // with ".x"; otherwise leave it untouched to avoid corrupting filenames
            // (e.g. renaming mymod.dll -> mymod.ll).
            string newExtension;
            if (IsEnabled && extension.StartsWith(".x", StringComparison.OrdinalIgnoreCase))
                newExtension = "." + extension.Substring(2);
            else if (!IsEnabled && !extension.StartsWith(".x", StringComparison.OrdinalIgnoreCase))
                newExtension = ".x" + extension.Substring(1);
            else
                newExtension = extension;

            // Already in the correct state
            if (string.Equals(extension, newExtension, StringComparison.OrdinalIgnoreCase))
                return true;

            string newPath = Path.Combine(directory, fileName + newExtension);

            if (File.Exists(newPath))
            {
                // Both .dll and .xdll exist simultaneously — back up the destination
                // instead of silently deleting it, to prevent data loss.
                string backupPath = newPath + ".bak";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(newPath, backupPath);
                System.Diagnostics.Debug.WriteLine(
                    $"Backed up existing file to {backupPath} before toggling {Name}");
            }

            File.Move(FullPath, newPath);
            FullPath = newPath;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error toggling mod {Name}: {ex.Message}");
            return false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
