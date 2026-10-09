using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;

namespace EasyTurn;

/// <summary>A selectable language shown in the settings drop-down.</summary>
public sealed class LanguageOption
{
    public string Code { get; }
    public string NativeName { get; }

    public LanguageOption(string code, string nativeName)
    {
        Code = code;
        NativeName = nativeName;
    }
}

/// <summary>
/// Application-wide localization service. XAML binds to the indexer with
/// <c>{Binding [key], Source={x:Static local:Localization.Instance}}</c>, and
/// switching <see cref="Language"/> raises a change notification for the indexer
/// so every bound label updates immediately.
/// </summary>
public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Instance { get; } = new();

    public const string DefaultLanguage = "en";

    public static IReadOnlyList<LanguageOption> Languages { get; } = new List<LanguageOption>
    {
        new("en", "English"),
        new("ar", "العربية"),
        new("tr", "Türkçe"),
        new("es", "Español"),
        new("ru", "Русский"),
        new("zh", "中文"),
        new("ja", "日本語"),
        new("de", "Deutsch"),
        new("pt", "Português"),
        new("id", "Bahasa Indonesia"),
        new("hi", "हिन्दी"),
        new("ur", "اردو"),
        new("bn", "বাংলা"),
    };

    private string _language = DefaultLanguage;

    public string Language
    {
        get => _language;
        set
        {
            string normalized = Normalize(value);
            if (_language == normalized)
                return;

            _language = normalized;
            // "Item[]" is the property name WPF listens to for indexer bindings.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
        }
    }

    public FlowDirection FlowDirection =>
        _language is "ar" or "ur" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string this[string key] => Translate(key);

    public static string T(string key) => Instance[key];

    public static string F(string key, params object?[] args)
    {
        string text = Instance[key];
        try
        {
            return string.Format(text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return DefaultLanguage;

        foreach (var language in Languages)
        {
            if (string.Equals(language.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
                return language.Code;
        }
        return DefaultLanguage;
    }

    private static string Translate(string key)
    {
        if (Translations.All.TryGetValue(Instance._language, out var table) &&
            table.TryGetValue(key, out var text))
        {
            return text;
        }

        return Translations.All[DefaultLanguage].TryGetValue(key, out var fallback) ? fallback : key;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
