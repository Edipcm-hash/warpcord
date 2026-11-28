using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WarpCord
{
    /// <summary>Embedded logo bitmaps, loaded once and frozen for cross-thread use.</summary>
    internal static class Assets
    {
        private static BitmapSource _logo256;
        private static BitmapSource _logo64;
        private static BitmapSource _logo32;

        public static BitmapSource Logo256
        {
            get
            {
                if (_logo256 == null) _logo256 = Load("WarpCord.Assets.warpcord-256.png");
                return _logo256;
            }
        }

        public static BitmapSource Logo64
        {
            get
            {
                if (_logo64 == null) _logo64 = Load("WarpCord.Assets.warpcord-64.png");
                return _logo64;
            }
        }

        public static BitmapSource Logo32
        {
            get
            {
                if (_logo32 == null) _logo32 = Load("WarpCord.Assets.warpcord-32.png");
                return _logo32;
            }
        }

        private static BitmapSource Load(string name)
        {
            var asm = typeof(Assets).Assembly;
            using (var stream = asm.GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("Gorsel kaynagi bulunamadi: " + name);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }
    }

    /// <summary>Fixed colours used by the live status widget.</summary>
    internal static class Palette
    {
        public static readonly SolidColorBrush Green = Make(0x34, 0xD3, 0x99);
        public static readonly SolidColorBrush Amber = Make(0xFB, 0xBF, 0x24);
        public static readonly SolidColorBrush Red = Make(0xF8, 0x71, 0x71);
        public static readonly SolidColorBrush Cyan = Make(0x22, 0xD3, 0xEE);
        public static readonly SolidColorBrush Violet = Make(0xA7, 0x8B, 0xFA);
        public static readonly SolidColorBrush Faint = Make(0x66, 0x70, 0x8C);
        public static readonly SolidColorBrush Dim = Make(0x98, 0xA2, 0xBC);
        public static readonly SolidColorBrush Text = Make(0xE9, 0xED, 0xF9);
        public static readonly SolidColorBrush White = Make(0xFF, 0xFF, 0xFF);

        public static SolidColorBrush Make(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
