using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WarpCord
{
    /// <summary>Small dark modal dialogs, built in code so they follow the theme.</summary>
    internal static class Dialogs
    {
        public static bool Confirm(Window owner, string title, string message, string okText, bool danger)
        {
            Window dialog = CreateWindow(owner, title, 480);
            Border card = CreateCard();
            var stack = new StackPanel();

            stack.Children.Add(CreateHeading(title));
            stack.Children.Add(CreateBody(message));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };

            var cancel = new Button
            {
                Content = "Vazgeç",
                Style = WindowFx.StyleFromResource("GhostButton"),
                Width = 104,
                IsCancel = true
            };
            cancel.Click += delegate { dialog.DialogResult = false; };

            var ok = new Button
            {
                Content = okText,
                Style = WindowFx.StyleFromResource(danger ? "DangerButton" : "PrimaryButton"),
                Width = 132,
                Height = 38,
                Margin = new Thickness(10, 0, 0, 0)
            };
            ok.Click += delegate { dialog.DialogResult = true; };

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            stack.Children.Add(buttons);

            card.Child = stack;
            dialog.Content = card;
            dialog.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) dialog.DialogResult = false;
            };
            dialog.Loaded += delegate { cancel.Focus(); };

            return dialog.ShowDialog() == true;
        }

        /// <summary>One-button message box for explanations that have no choice.</summary>
        public static void Info(Window owner, string title, string message)
        {
            Window dialog = CreateWindow(owner, title, 520);
            Border card = CreateCard();
            var stack = new StackPanel();

            stack.Children.Add(CreateHeading(title));
            stack.Children.Add(CreateBody(message));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            var ok = new Button
            {
                Content = "Tamam",
                Style = WindowFx.StyleFromResource("PrimaryButton"),
                Width = 120,
                Height = 38,
                IsDefault = true
            };
            ok.Click += delegate { dialog.DialogResult = true; };
            buttons.Children.Add(ok);
            stack.Children.Add(buttons);

            card.Child = stack;
            dialog.Content = card;
            dialog.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) dialog.DialogResult = false;
            };
            dialog.Loaded += delegate { ok.Focus(); };

            dialog.ShowDialog();
        }

        private static Window CreateWindow(Window owner, string title, double width)
        {
            var dialog = new Window();
            dialog.Title = title;
            dialog.WindowStyle = WindowStyle.None;
            dialog.ResizeMode = ResizeMode.NoResize;
            dialog.SizeToContent = SizeToContent.Height;
            dialog.Width = width;
            dialog.WindowStartupLocation = owner != null && owner.IsVisible
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = false;
            dialog.Background = WindowFx.BrushFromResource("BgBrush", Brushes.Black);
            dialog.FontFamily = owner != null ? owner.FontFamily : new FontFamily("Segoe UI");
            dialog.Foreground = WindowFx.BrushFromResource("TextBrush", Brushes.White);
            if (owner != null && owner.IsVisible) dialog.Owner = owner;
            WindowFx.Apply(dialog);
            return dialog;
        }

        private static Border CreateCard()
        {
            return new Border
            {
                Background = WindowFx.BrushFromResource("PanelBrush", Brushes.DimGray),
                BorderBrush = WindowFx.BrushFromResource("StrokeBrush", Brushes.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(22, 20, 22, 18)
            };
        }

        private static TextBlock CreateHeading(string title)
        {
            return new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = WindowFx.BrushFromResource("TextBrush", Brushes.White)
            };
        }

        private static TextBlock CreateBody(string message)
        {
            return new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 12.5,
                LineHeight = 19,
                TextWrapping = TextWrapping.Wrap,
                Foreground = WindowFx.BrushFromResource("TextDimBrush", Brushes.LightGray)
            };
        }
    }
}
