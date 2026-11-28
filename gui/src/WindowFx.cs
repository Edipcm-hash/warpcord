using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace WarpCord
{
    /// <summary>Native window niceties: rounded corners, dark frame, custom chrome.</summary>
    internal static class WindowFx
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Window window)
        {
            // borderless content, but keep the OS resize border so snapping works
            var chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(window, chrome);

            window.SourceInitialized += delegate
            {
                try
                {
                    IntPtr handle = new WindowInteropHelper(window).Handle;
                    if (handle == IntPtr.Zero) return;
                    int round = DwmwcpRound;
                    DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
                    int dark = 1;
                    DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
                }
                catch
                {
                    // Windows 10 has no corner preference; square corners are fine there
                }
            };
        }

        public static Brush BrushFromResource(string key, Brush fallback)
        {
            var brush = Application.Current != null ? Application.Current.Resources[key] as Brush : null;
            return brush ?? fallback;
        }

        public static Style StyleFromResource(string key)
        {
            return Application.Current != null ? Application.Current.Resources[key] as Style : null;
        }
    }
}
