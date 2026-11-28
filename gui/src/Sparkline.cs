using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WarpCord
{
    /// <summary>Small latency history line for the status card.</summary>
    public sealed class Sparkline : FrameworkElement
    {
        private readonly List<double> _values = new List<double>();
        private readonly Brush _stroke;
        private readonly Brush _fill;

        public Sparkline()
        {
            _stroke = Palette.Cyan;
            _fill = new SolidColorBrush(Color.FromArgb(0x38, 0x22, 0xD3, 0xEE));
            _fill.Freeze();
            Capacity = 30;
            SnapsToDevicePixels = true;
        }

        public int Capacity { get; set; }

        public int Count
        {
            get { return _values.Count; }
        }

        public void Add(double value)
        {
            if (value <= 0) return;
            _values.Add(value);
            while (_values.Count > Capacity) _values.RemoveAt(0);
            InvalidateVisual();
        }

        public void Clear()
        {
            _values.Clear();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 8 || height < 6) return;
            if (_values.Count < 2)
            {
                // a flat baseline while there is nothing to plot
                dc.DrawLine(new Pen(_fill, 1), new Point(0, height - 3), new Point(width, height - 3));
                return;
            }

            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (double value in _values)
            {
                if (value < min) min = value;
                if (value > max) max = value;
            }
            if (max - min < 20) { max = min + 20; }

            var line = new StreamGeometry();
            using (StreamGeometryContext ctx = line.Open())
            {
                ctx.BeginFigure(PointAt(0, min, max, width, height), false, false);
                for (int i = 1; i < _values.Count; i++)
                    ctx.LineTo(PointAt(i, min, max, width, height), true, false);
            }
            line.Freeze();

            var area = new StreamGeometry();
            using (StreamGeometryContext ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, height), true, true);
                for (int i = 0; i < _values.Count; i++)
                    ctx.LineTo(PointAt(i, min, max, width, height), true, false);
                ctx.LineTo(new Point(PointAt(_values.Count - 1, min, max, width, height).X, height), true, false);
            }
            area.Freeze();

            dc.DrawGeometry(_fill, null, area);
            dc.DrawGeometry(null, new Pen(_stroke, 1.3), line);

            Point last = PointAt(_values.Count - 1, min, max, width, height);
            dc.DrawEllipse(_stroke, null, last, 1.9, 1.9);
        }

        private Point PointAt(int index, double min, double max, double width, double height)
        {
            double x = _values.Count <= 1 ? 0 : width * index / (_values.Count - 1);
            double range = max - min;
            double normalized = (_values[index] - min) / (range <= 0 ? 1 : range);
            double y = height - 3 - normalized * (height - 6);
            return new Point(x, y);
        }
    }
}
