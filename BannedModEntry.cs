using System.ComponentModel;

namespace EasyTurn;

/// <summary>
/// A monitored mod that can be ticked to ban it. A banned mod is turned off for
/// good: it disappears from the main screen and the "Mods" toggle never turns it
/// back on. Replaces the old "Mods" list in the settings panel.
/// </summary>
public class BannedModEntry : INotifyPropertyChanged
{
    public string Name { get; }

    private bool _isBanned;
    public bool IsBanned
    {
        get => _isBanned;
        set
        {
            if (_isBanned == value) return;
            _isBanned = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBanned)));
        }
    }

    public BannedModEntry(string name, bool isBanned)
    {
        Name = name;
        _isBanned = isBanned;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
