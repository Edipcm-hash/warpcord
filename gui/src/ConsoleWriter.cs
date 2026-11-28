using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WarpCord
{
    /// <summary>Appends coloured lines to the log pane with a bounded scrollback.</summary>
    internal sealed class ConsoleWriter
    {
        private const int MaxBlocks = 2000;
        private readonly RichTextBox _box;

        public ConsoleWriter(RichTextBox box)
        {
            _box = box;
            Autoscroll = true;
            _box.Document.Blocks.Clear();
        }

        public bool Autoscroll { get; set; }

        public bool IsEmpty
        {
            get { return _box.Document.Blocks.Count == 0; }
        }

        public void Write(string text, Brush brush, bool bold)
        {
            if (text == null) text = "";
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
                LineHeight = 16.5,
                TextAlignment = TextAlignment.Left
            };
            var run = new Run(text);
            if (brush != null) run.Foreground = brush;
            if (bold) run.FontWeight = FontWeights.SemiBold;
            paragraph.Inlines.Add(run);
            _box.Document.Blocks.Add(paragraph);

            while (_box.Document.Blocks.Count > MaxBlocks)
                _box.Document.Blocks.Remove(_box.Document.Blocks.FirstBlock);

            if (Autoscroll) _box.ScrollToEnd();
        }

        public void Write(LogLine line)
        {
            switch (line.Kind)
            {
                case LogKind.Ok:
                    Write(line.Text, Palette.Green, false);
                    break;
                case LogKind.Error:
                    Write(line.Text, Palette.Red, false);
                    break;
                case LogKind.Warn:
                    Write(line.Text, Palette.Amber, false);
                    break;
                case LogKind.Step:
                    Write(line.Text, Palette.Cyan, false);
                    break;
                case LogKind.Header:
                    Write("  " + line.Text, Palette.Violet, true);
                    break;
                case LogKind.Dim:
                    Write(line.Text, Palette.Faint, false);
                    break;
                default:
                    Write(line.Text, Palette.Dim, false);
                    break;
            }
        }

        public void Separator(string title)
        {
            Write("", null, false);
            Write("▸ " + title, Palette.Text, true);
        }

        public void Clear()
        {
            _box.Document.Blocks.Clear();
        }

        public string Dump()
        {
            var range = new TextRange(_box.Document.ContentStart, _box.Document.ContentEnd);
            return range.Text;
        }
    }
}
