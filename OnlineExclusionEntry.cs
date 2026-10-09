using System.ComponentModel;

namespace EasyTurn;

/// <summary>
/// A monitored file that can be ticked to stay disabled while "Online Ready" is
/// active. Replaces the old free-text online exclusion box.
/// </summary>
public class OnlineExclusionEntry : INotifyPropertyChanged
{
    public string Name { get; }

    private bool _isExcluded;
    public bool IsExcluded
    {
        get => _isExcluded;
        set
        {
            if (_isExcluded == value) return;
            _isExcluded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExcluded)));
        }
    }

    public OnlineExclusionEntry(string name, bool isExcluded)
    {
        Name = name;
        _isExcluded = isExcluded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
