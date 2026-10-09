using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyTurn;

/// <summary>
/// A Photoshop-style colour picker: a saturation/value square with a hue bar, an
/// alpha bar, and hex + R/G/B/A inputs. Any colour can be reached.
/// </summary>
public partial class ColorPickerDialog : Window
{
    private double _hue;
    private double _saturation = 1;
    private double _value = 1;
    private double _alpha = 1;

    // Guards the colour <-> text update cycle so typing does not fight with it.
    private bool _updating;

    private readonly Color _original;

    /// <summary>The picked colour, or null when the dialog was cancelled.</summary>
    public Color? Result { get; private set; }

    /// <summary>Alpha the user chose, 0..1.</summary>
    public double ResultAlpha { get; private set; }

    public ColorPickerDialog(Color initial, double initialAlpha)
    {
        InitializeComponent();

        ApplyLocalizedText();

        _original = initial;
        _alpha = Math.Clamp(initialAlpha, 0, 1);
        PreviewOld.Background = new SolidColorBrush(initial);
        PreviewNew.Background = new SolidColorBrush(initial);

        SetColor(initial);
        UpdateHueLayer();
        SyncAllControls();
    }

    private void ApplyLocalizedText()
    {
        Title = Localization.T("colorPickerTitle");
        BtnOk.Content = Localization.T("ok");
        BtnCancel.Content = Localization.T("cancel");
        LblHex.Text = Localization.T("hex");
        LblRed.Text = Localization.T("red");
        LblGreen.Text = Localization.T("green");
        LblBlue.Text = Localization.T("blue");
        LblAlpha.Text = Localization.T("alpha");
        LblAlphaValue.Text = Localization.T("alpha");
    }

    // ---- picking with the mouse -------------------------------------------------

    private void Sv_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SvArea.CaptureMouse();
        UpdateSaturationValue(e.GetPosition(SvArea));
    }

    private void Sv_MouseMove(object sender, MouseEventArgs e)
    {
        if (SvArea.IsMouseCaptured)
            UpdateSaturationValue(e.GetPosition(SvArea));
    }

    private void Sv_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SvArea.ReleaseMouseCapture();
        UpdateSaturationValue(e.GetPosition(SvArea));
    }

    private void UpdateSaturationValue(Point point)
    {
        _saturation = Math.Clamp(point.X / SvArea.ActualWidth, 0, 1);
        _value = 1 - Math.Clamp(point.Y / SvArea.ActualHeight, 0, 1);
        SyncAllControls();
    }

    private void Hue_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HueArea.CaptureMouse();
        UpdateHue(e.GetPosition(HueArea));
    }

    private void Hue_MouseMove(object sender, MouseEventArgs e)
    {
        if (HueArea.IsMouseCaptured)
            UpdateHue(e.GetPosition(HueArea));
    }

    private void Hue_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        HueArea.ReleaseMouseCapture();
        UpdateHue(e.GetPosition(HueArea));
    }

    private void UpdateHue(Point point)
    {
        _hue = Math.Clamp(point.Y / HueArea.ActualHeight, 0, 1) * 360;
        UpdateHueLayer();
        SyncAllControls();
    }

    private void Alpha_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        AlphaArea.CaptureMouse();
        UpdateAlpha(e.GetPosition(AlphaArea));
    }

    private void Alpha_MouseMove(object sender, MouseEventArgs e)
    {
        if (AlphaArea.IsMouseCaptured)
            UpdateAlpha(e.GetPosition(AlphaArea));
    }

    private void Alpha_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        AlphaArea.ReleaseMouseCapture();
        UpdateAlpha(e.GetPosition(AlphaArea));
    }

    private void UpdateAlpha(Point point)
    {
        _alpha = Math.Clamp(point.X / AlphaArea.ActualWidth, 0, 1);
        SyncAllControls();
    }

    // ---- typed input ------------------------------------------------------------

    private void Hex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating)
            return;

        string text = TxtHex.Text.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;

        try
        {
            if (ColorConverter.ConvertFromString(text) is Color parsed)
            {
                // A bare #RRGGBB leaves the chosen alpha untouched.
                _updating = true;
                SetColor(parsed);
                _updating = false;
                SyncAllControls();
            }
        }
        catch (Exception)
        {
            // Partially typed value — wait for a valid one.
        }
    }

    private void Rgba_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating)
            return;

        if (!TryReadByte(TxtRed.Text, out int red) ||
            !TryReadByte(TxtGreen.Text, out int green) ||
            !TryReadByte(TxtBlue.Text, out int blue) ||
            !TryReadByte(TxtAlpha.Text, out int alpha))
        {
            return;
        }

        _updating = true;
        SetColor(Color.FromRgb((byte)red, (byte)green, (byte)blue));
        _alpha = alpha / 255.0;
        _updating = false;
        SyncAllControls();
    }

    private static bool TryReadByte(string? text, out int value)
        => int.TryParse((text ?? string.Empty).Trim(), out value) && value is >= 0 and <= 255;

    // ---- state ------------------------------------------------------------------

    private void SetColor(Color color)
    {
        RgbToHsv(color, out double hue, out double saturation, out double value);
        // Keep the current hue when the colour is a shade of grey (hue undefined).
        if (saturation > 0)
            _hue = hue;
        _saturation = saturation;
        _value = value;
        UpdateHueLayer();
    }

    private Color CurrentColor()
    {
        Color rgb = HsvToRgb(_hue, _saturation, _value);
        return Color.FromArgb((byte)Math.Round(_alpha * 255), rgb.R, rgb.G, rgb.B);
    }

    // Repaints every control from the current H/S/V/A values.
    private void SyncAllControls()
    {
        Color color = CurrentColor();
        PreviewNew.Background = new SolidColorBrush(color);

        PositionThumb(SvThumb, _saturation * SvArea.ActualWidth, (1 - _value) * SvArea.ActualHeight);
        PositionThumb(HueThumb, 0, _hue / 360 * HueArea.ActualHeight);
        PositionThumb(AlphaThumb, _alpha * AlphaArea.ActualWidth, 0);

        AlphaGradient.Background = new LinearGradientBrush(
            Color.FromArgb(0, color.R, color.G, color.B),
            Color.FromArgb(255, color.R, color.G, color.B),
            new Point(0, 0), new Point(1, 0));

        _updating = true;
        TxtHex.Text = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        TxtRed.Text = color.R.ToString();
        TxtGreen.Text = color.G.ToString();
        TxtBlue.Text = color.B.ToString();
        TxtAlpha.Text = color.A.ToString();
        _updating = false;
    }

    // Centres a thumb on the given point inside its canvas.
    private static void PositionThumb(FrameworkElement thumb, double x, double y)
    {
        thumb.Margin = new Thickness(
            x - thumb.Width / 2,
            y - thumb.Height / 2,
            0,
            0);
    }

    private void UpdateHueLayer() => SvHueLayer.Background = new SolidColorBrush(HsvToRgb(_hue, 1, 1));

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = CurrentColor();
        ResultAlpha = _alpha;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
        Close();
    }

    // ---- colour maths ------------------------------------------------------------

    private static Color HsvToRgb(double hue, double saturation, double value)
    {
        double c = value * saturation;
        double h = hue / 60.0;
        double x = c * (1 - Math.Abs(h % 2 - 1));
        double m = value - c;

        (double r, double g, double b) = h switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        value = max;
        saturation = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            hue = 0;
            return;
        }

        hue = max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);

        if (hue < 0)
            hue += 360;
    }
}
