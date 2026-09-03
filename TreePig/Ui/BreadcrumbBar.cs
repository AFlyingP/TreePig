using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TreePig.Core;

namespace TreePig.Ui
{
    // shows the path of the selected row as clickable pieces so it is always
    // obvious which folder you are in and how you got there
    class BreadcrumbBar : Control
    {
        const int Pad = 6;
        const int ChevronW = 13;
        const int DotsW = 18;

        readonly List<FsNode> _path = new List<FsNode>();
        readonly List<KeyValuePair<FsNode, Rectangle>> _hits
            = new List<KeyValuePair<FsNode, Rectangle>>();
        Point _mouse;

        public event Action<FsNode> SegmentClicked;

        public BreadcrumbBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Height = 28;
        }

        public IReadOnlyList<FsNode> Path => _path;

        // chain ordered from the root down to the selection, null clears it
        public void SetPath(FsNode node)
        {
            _path.Clear();
            for (var n = node; n != null; n = n.Parent) _path.Add(n);
            _path.Reverse();
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Height = Font.Height + 14;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(Color.FromArgb(248, 249, 250)))
                g.FillRectangle(bg, ClientRectangle);

            _hits.Clear();
            if (_path.Count == 0) { DrawBottomLine(g); return; }

            var widths = new int[_path.Count];
            int total = 0;
            for (int i = 0; i < _path.Count; i++)
            {
                widths[i] = TextRenderer.MeasureText(_path[i].Name, Font).Width + 12;
                total += widths[i] + (i > 0 ? ChevronW : 0);
            }

            // longest paths drop their oldest pieces on the left, the deepest
            // ones are the ones you actually need
            int first = 0;
            while (total + DotsW > Width - Pad * 2 && first < _path.Count - 1)
            {
                total -= widths[first] + ChevronW;
                first++;
            }
            bool truncated = first > 0;

            int x = Pad;
            if (truncated)
            {
                TextRenderer.DrawText(g, "...", Font, new Rectangle(x, 0, DotsW, Height),
                    Color.FromArgb(120, 120, 120),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                x += DotsW;
                DrawChevron(g, x);
                x += ChevronW;
            }

            for (int i = first; i < _path.Count; i++)
            {
                var rect = new Rectangle(x, 0, widths[i], Height);
                _hits.Add(new KeyValuePair<FsNode, Rectangle>(_path[i], rect));

                bool hover = rect.Contains(_mouse);
                if (hover)
                    using (var b = new SolidBrush(Color.FromArgb(232, 240, 251)))
                        g.FillRectangle(b, rect);
                TextRenderer.DrawText(g, _path[i].Name, Font, new Rectangle(x + 6, 0, widths[i] - 12, Height),
                    hover ? Color.FromArgb(0, 90, 180) : SystemColors.ControlText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                x += widths[i];

                if (i < _path.Count - 1)
                {
                    DrawChevron(g, x);
                    x += ChevronW;
                }
            }
            DrawBottomLine(g);
        }

        void DrawChevron(Graphics g, int x)
        {
            TextRenderer.DrawText(g, "\x203A", Font, new Rectangle(x, 0, ChevronW, Height),
                Color.FromArgb(140, 140, 140),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        void DrawBottomLine(Graphics g)
        {
            using (var p = new Pen(Color.FromArgb(224, 226, 229)))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        FsNode PartAt(Point pt)
        {
            foreach (var kv in _hits)
                if (kv.Value.Contains(pt)) return kv.Key;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = PartAt(e.Location) != null;
            if (Cursor != (over ? Cursors.Hand : Cursors.Default))
                Cursor = over ? Cursors.Hand : Cursors.Default;
            if (_mouse != e.Location)
            {
                _mouse = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouse = new Point(-1, -1);
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var node = PartAt(e.Location);
            if (node != null) SegmentClicked?.Invoke(node);
        }
    }
}
