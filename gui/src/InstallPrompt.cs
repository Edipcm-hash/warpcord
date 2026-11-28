using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WarpCord
{
    internal enum InstallMethodChoice
    {
        App,
        Manual,
        Cancel
    }

    /// <summary>
    /// "Install with the app or by hand?" — shown on first run when nothing is
    /// installed yet, and reachable again from the advanced panel.
    /// </summary>
    internal static class InstallPrompt
    {
        public static InstallMethodChoice Show(Window owner)
        {
            Grid root;
            Border card = BuildCard(out root);

            var dialog = new Window();
            dialog.Title = "Kurulum yöntemi";
            dialog.WindowStyle = WindowStyle.None;
            dialog.ResizeMode = ResizeMode.NoResize;
            dialog.SizeToContent = SizeToContent.Height;
            dialog.Width = 522;
            dialog.WindowStartupLocation = owner != null && owner.IsVisible
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = false;
            dialog.Background = WindowFx.BrushFromResource("BgBrush", Brushes.Black);
            dialog.FontFamily = owner != null ? owner.FontFamily : new FontFamily("Segoe UI");
            dialog.Foreground = WindowFx.BrushFromResource("TextBrush", Brushes.White);
            if (owner != null && owner.IsVisible) dialog.Owner = owner;
            WindowFx.Apply(dialog);
            dialog.Content = card;

            InstallMethodChoice choice = InstallMethodChoice.Cancel;

            var useApp = (Button)root.FindName("UseAppBtn");
            var useManual = (Button)root.FindName("UseManualBtn");
            var cancel = (Button)root.FindName("CancelBtn");

            useApp.Click += delegate { choice = InstallMethodChoice.App; dialog.DialogResult = true; };
            useManual.Click += delegate { choice = InstallMethodChoice.Manual; dialog.DialogResult = true; };
            cancel.Click += delegate { choice = InstallMethodChoice.Cancel; dialog.DialogResult = false; };
            dialog.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    choice = InstallMethodChoice.Cancel;
                    dialog.DialogResult = false;
                }
            };
            dialog.Loaded += delegate { useApp.Focus(); };

            dialog.ShowDialog();
            return choice;
        }

        /// <summary>Draws the prompt to a PNG (documentation and visual regression checks).</summary>
        public static void RenderTo(string path, double scale)
        {
            Grid root;
            Border card = BuildCard(out root);
            card.Measure(new Size(522, double.PositiveInfinity));
            card.Arrange(new Rect(0, 0, 522, card.DesiredSize.Height));
            card.UpdateLayout();

            double width = 522;
            double height = card.ActualHeight > 0 ? card.ActualHeight : card.DesiredSize.Height;
            if (scale <= 0) scale = 2;

            var bitmap = new RenderTargetBitmap(
                (int)Math.Round(width * scale), (int)Math.Round(height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(card);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string full = System.IO.Path.GetFullPath(path);
            string dir = System.IO.Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (var stream = File.Create(full)) encoder.Save(stream);
        }

        private static Border BuildCard(out Grid root)
        {
            root = Xaml.Load<Grid>("WarpCord.Prompt.xaml");
            return new Border
            {
                Background = WindowFx.BrushFromResource("PanelBrush", Brushes.DimGray),
                BorderBrush = WindowFx.BrushFromResource("StrokeBrush", Brushes.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = root
            };
        }
    }
}
