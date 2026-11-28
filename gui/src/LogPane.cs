using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarpCord
{
    internal enum LogTab
    {
        Action,
        Tunnel,
        Repair
    }

    /// <summary>
    /// The log card: an in-memory action buffer plus live tails of tunnel.log and
    /// heal.log. Switching tabs re-renders from the right source.
    /// </summary>
    internal sealed class LogPane
    {
        private sealed class Entry
        {
            public string Text;
            public Brush Brush;
            public bool Bold;
        }

        private const int MaxEntries = 2000;
        private const int FileTail = 240;

        private readonly ConsoleWriter _writer;
        private readonly TextBlock _empty;
        private readonly List<Entry> _action = new List<Entry>();
        private readonly List<string> _fileLines = new List<string>();

        public LogPane(ConsoleWriter writer, TextBlock empty)
        {
            _writer = writer;
            _empty = empty;
            Autoscroll = true;
        }

        public bool Autoscroll
        {
            get { return _writer.Autoscroll; }
            set { _writer.Autoscroll = value; }
        }

        public LogTab Tab { get; private set; }

        public bool IsEmpty
        {
            get
            {
                if (Tab == LogTab.Action) return _action.Count == 0;
                return _fileLines.Count == 0;
            }
        }

        public void SetTab(LogTab tab)
        {
            Tab = tab;
            _fileLines.Clear();
            RenderAll();
        }

        public void Write(string text, Brush brush, bool bold)
        {
            if (text == null) text = "";
            if (Tab == LogTab.Action) _writer.Write(text, brush, bold);

            _action.Add(new Entry { Text = text, Brush = brush, Bold = bold });
            if (_action.Count > MaxEntries) _action.RemoveAt(0);
        }

        public void Write(LogLine line)
        {
            if (line == null) return;
            Brush brush = BrushFor(line.Kind);
            bool bold = line.Kind == LogKind.Header ||
                        (line.Kind == LogKind.Ok &&
                         line.Text.IndexOf("BASARILI", StringComparison.OrdinalIgnoreCase) >= 0);
            Write(line.Text, brush, bold);
        }

        public void Separator(string title)
        {
            Write("", null, false);
            Write("▸ " + title, Palette.Text, true);
        }

        public void Clear()
        {
            _action.Clear();
            _fileLines.Clear();
            _writer.Clear();
        }

        public string Dump()
        {
            return _writer.Dump();
        }

        /// <summary>Re-reads the tail of the selected file tab.</summary>
        public void RefreshFromFiles(string tunnelPath, string healPath)
        {
            if (Tab == LogTab.Action) return;
            string path = Tab == LogTab.Tunnel ? tunnelPath : healPath;
            string[] lines = Prober.Tail(path, FileTail);
            if (lines.Length == _fileLines.Count) return;   // cheap change detection

            _fileLines.Clear();
            _fileLines.AddRange(lines);
            RenderAll();
        }

        private void RenderAll()
        {
            _writer.Clear();

            if (Tab == LogTab.Action)
            {
                foreach (Entry entry in _action) _writer.Write(entry.Text, entry.Brush, entry.Bold);
            }
            else
            {
                foreach (string line in _fileLines)
                {
                    LogKind kind = ClassifyFileLine(line);
                    _writer.Write(line, BrushFor(kind), false);
                }
            }

            if (_empty != null)
                _empty.Visibility = IsEmpty ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private static LogKind ClassifyFileLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return LogKind.Dim;
            string value = line;
            if (value.IndexOf("[ OK ]", StringComparison.OrdinalIgnoreCase) >= 0) return LogKind.Ok;
            if (value.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("HATA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("error", StringComparison.Ordinal) >= 0) return LogKind.Error;
            if (value.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("UYARI", StringComparison.OrdinalIgnoreCase) >= 0) return LogKind.Warn;
            if (value.StartsWith("===") || value.StartsWith("---")) return LogKind.Header;
            return LogKind.Info;
        }

        private static Brush BrushFor(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Ok: return Palette.Green;
                case LogKind.Error: return Palette.Red;
                case LogKind.Warn: return Palette.Amber;
                case LogKind.Step: return Palette.Cyan;
                case LogKind.Header: return Palette.Violet;
                case LogKind.Dim: return Palette.Faint;
                default: return Palette.Dim;
            }
        }
    }
}
