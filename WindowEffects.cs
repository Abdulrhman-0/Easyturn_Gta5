using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace EasyTurn;

/// <summary>
/// Applies a blur to what is behind the window (the frosted-glass effect used by
/// the Appearance settings). This is an undocumented but long-standing Windows
/// API; every call is guarded so an unsupported system simply keeps the normal
/// transparent background instead.
/// </summary>
public static class WindowEffects
{
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;

    private const int WindowCompositionAttributeAccentPolicy = 19;

    public static void ApplyBlur(Window window, int blurRadius, Color color, double opacity)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;

            // WPF's transparency is what lets the blurred background show through.
            var accent = new AccentPolicy
            {
                AccentState = blurRadius > 0 ? AccentEnableBlurBehind : AccentDisabled,
                // 0x02 | 0x20 keeps the blur inside the window's own bounds.
                AccentFlags = blurRadius > 0 ? 0x02 | 0x20 : 0,
                GradientColor = ToAbgr(color, opacity),
                AnimationId = 0,
            };

            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr accentPointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPointer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttributeAccentPolicy,
                    Data = accentPointer,
                    SizeOfData = size,
                };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPointer);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply window blur: {ex.Message}");
        }
    }

    // The accent colour is packed as 0xAABBGGRR (not WPF's 0xAARRGGBB).
    private static int ToAbgr(Color color, double opacity)
    {
        byte alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
        return (alpha << 24) | (color.B << 16) | (color.G << 8) | color.R;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
}
