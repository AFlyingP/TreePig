using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TreePig.Core;

namespace TreePig.Ui
{
    class TreeColumn
    {
        public const int Name = 0, Size = 1, Allocated = 2, Files = 3,
                         Folders = 4, Percent = 5, LastChange = 6, Owner = 7;

        public string Title;
        public int Width;
        public HorizontalAlignment Align;
        public bool Visible = true;
        public int MinWidth = 36;

        public TreeColumn(string title, int width, HorizontalAlignment align)
        {
            Title = title;
            Width = width;
            Align = align;
        }
    }

    class TreeColumnClickEventArgs : EventArgs
    {
        public int Column;
        public TreeColumnClickEventArgs(int column) { Column = column; }
    }

    // tree on the left, columns on the right. The TreeView draws every row
    // itself so we can put size bars behind the numbers like TreeSize does.
    class TreeListView : UserControl
    {
        public readonly List<TreeColumn> Columns = new List<TreeColumn>
        {
            new TreeColumn("Name",        300, HorizontalAlignment.Left),
            new TreeColumn("Size",        115, HorizontalAlignment.Right),
            new TreeColumn("Allocated",   115, HorizontalAlignment.Right),
            new TreeColumn("Files",        70, HorizontalAlignment.Right),
            new TreeColumn("Folders",      70, HorizontalAlignment.Right),
            new TreeColumn("Percent",      90, HorizontalAlignment.Right),
            new TreeColumn("Last Change", 135, HorizontalAlignment.Left),
            new TreeColumn("Owner",       150, HorizontalAlignment.Left),
        };

        public int SortColumn = TreeColumn.Size;
        public bool SortAscending = false;
        public bool ShowBars = true;
        public Color BarColor = Color.FromArgb(110, 192, 80, 77);
        public SizeUnit Unit = SizeUnit.Auto;

        public event EventHandler<TreeColumnClickEventArgs> ColumnClicked;
        public event EventHandler HeaderRightClicked;
        public event EventHandler SelectionChanged;
        public event EventHandler NodeActivated;      // enter or double click
        public event EventHandler DeleteRequested;    // del key

        readonly HeaderStrip _header;
        readonly BreadcrumbBar _breadcrumb;
        readonly ColumnTree _tree;
        readonly System.Windows.Forms.Timer _layoutTimer;
        readonly Dictionary<FsNode, TreeNode> _map = new Dictionary<FsNode, TreeNode>();
        readonly HashSet<FsNode> _populated = new HashSet<FsNode>();
        HashSet<FsNode> _chain = new HashSet<FsNode>();
        FsNode _selected;
        List<KeyValuePair<int, int>> _colPos = new List<KeyValuePair<int, int>>();
        int _colTotal;
        int _spaceW;

        public FsNode RootFs { get; private set; }

        public TreeListView()
        {
            _breadcrumb = new BreadcrumbBar { Dock = DockStyle.Top };
            _header = new HeaderStrip(this) { Dock = DockStyle.Top };
            _tree = new ColumnTree(this) { Dock = DockStyle.Fill };
            Controls.Add(_tree);
            Controls.Add(_header);
            Controls.Add(_breadcrumb);
            _breadcrumb.SegmentClicked += n => SelectNode(n);

            _layoutTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _layoutTimer.Tick += LayoutTimerTick;

            RecalcColumns();
            UpdateSpaceWidth();
        }

        // --- layout helpers ---

        void UpdateSpaceWidth()
        {
            _spaceW = Math.Max(1, TextRenderer.MeasureText(" m", Font).Width - TextRenderer.MeasureText("m", Font).Width);
        }

        void RecalcColumns()
        {
            _colPos.Clear();
            _colTotal = 0;
            for (int i = 0; i < Columns.Count; i++)
            {
                if (!Columns[i].Visible) continue;
                _colPos.Add(new KeyValuePair<int, int>(i, _colTotal));
                _colTotal += Columns[i].Width;
            }
            _header.Height = Font.Height + 11;
            _header.Invalidate();
            _tree.Invalidate();
            StartLayoutTimer();
        }

        void StartLayoutTimer()
        {
            _layoutTimer.Stop();
            _layoutTimer.Start();
        }

        void LayoutTimerTick(object sender, EventArgs e)
        {
            _layoutTimer.Stop();
            RepadAll();
            _tree.Invalidate();
        }

        int ColumnsTotal()
        {
            int t = 0;
            foreach (var c in Columns) if (c.Visible) t += c.Width;
            return t;
        }

        string PadFor(FsNode fs)
        {
            // trailing spaces stretch the item the control measures, that is
            // what keeps the horizontal scrollbar able to reach the last
            // column and full row painting working
            int target = Math.Max(_colTotal, _tree.ClientSize.Width) + 32;
            int labelW = fs.Name.Length == 0 ? 0 : TextRenderer.MeasureText(fs.Name, Font).Width;
            int need = target - labelW - 48;
            if (need <= 0) return " ";
            return " " + new string(' ', (int)Math.Ceiling(need / (double)_spaceW));
        }

        void RepadAll()
        {
            if (RootFs == null || _tree.Nodes.Count == 0) return;
            _tree.BeginUpdate();
            RepadRecursive(_tree.Nodes);
            _tree.EndUpdate();
        }

        void RepadRecursive(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                var fs = (FsNode)n.Tag;
                n.Text = fs.Name + PadFor(fs);
                if (n.Nodes.Count > 0) RepadRecursive(n.Nodes);
            }
        }

        // --- data ---

        // The tree only builds TreeNodes for rows that are actually shown.
        // A scan of a big drive can easily have hundreds of thousands of
        // folders, building a control node for each of them froze the UI for
        // ages. Expansion pulls in the next level on demand instead.

        public void SetRoot(FsNode root)
        {
            RootFs = root;
            Reload();
            if (_tree.Nodes.Count > 0) _tree.Nodes[0].Expand();
            UpdateChain();
        }

        public void Reload()
        {
            _tree.BeginUpdate();
            var expanded = new HashSet<string>();
            CollectExpanded(_tree.Nodes, expanded);
            string sel = _tree.SelectedNode != null ? ((FsNode)_tree.SelectedNode.Tag)?.FullName : null;

            _tree.Nodes.Clear();
            _map.Clear();
            _populated.Clear();
            if (RootFs != null)
            {
                var rootTn = MakeNode(RootFs);
                _tree.Nodes.Add(rootTn);
                RestoreExpanded(RootFs, rootTn, expanded);
            }
            _tree.EndUpdate();

            if (sel != null) RestoreSelection(sel);
            UpdateChain();
        }

        TreeNode MakeNode(FsNode fs)
        {
            var tn = new TreeNode(fs.Name + PadFor(fs)) { Tag = fs };
            _map[fs] = tn;
            if (fs.HasChildren)
                tn.Nodes.Add(DummyNode());
            return tn;
        }

        // placeholder child, that is what makes the control offer expansion;
        // it gets swapped for the real rows when the node is opened
        static TreeNode DummyNode() => new TreeNode("...") { Tag = null };

        void PopulateNode(TreeNode tn, FsNode fs)
        {
            if (fs == null || !fs.HasChildren || _populated.Contains(fs)) return;
            _populated.Add(fs);
            tn.Nodes.Clear();
            fs.Children.Sort(CompareNodes);
            foreach (var c in fs.Children)
                tn.Nodes.Add(MakeNode(c));
        }

        void RestoreExpanded(FsNode fs, TreeNode tn, HashSet<string> set)
        {
            if (!set.Contains(fs.FullName) || !fs.HasChildren) return;
            if (tn.Nodes.Count == 1 && tn.Nodes[0].Tag == null) tn.Nodes.RemoveAt(0);
            PopulateNode(tn, fs);
            tn.Expand();
            foreach (var c in fs.Children)
                if (_map.TryGetValue(c, out TreeNode ctn))
                    RestoreExpanded(c, ctn, set);
        }

        void CollectExpanded(TreeNodeCollection nodes, HashSet<string> set)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.IsExpanded) set.Add(((FsNode)n.Tag).FullName);
                if (n.Nodes.Count > 0) CollectExpanded(n.Nodes, set);
            }
        }

        void RestoreSelection(string fullName)
        {
            foreach (var kv in _map)
            {
                if (kv.Key.FullName == fullName)
                {
                    _tree.SelectedNode = kv.Value;
                    kv.Value.EnsureVisible();
                    return;
                }
            }
        }

        // sorts what is on screen; folders get sorted when their rows are
        // first built, so a sort click stays cheap no matter how big the
        // scan is
        public void SortModel()
        {
            foreach (var fs in _map.Keys)
                if (fs.HasChildren)
                    fs.Children.Sort(CompareNodes);
        }

        int CompareNodes(FsNode a, FsNode b)
        {
            int r;
            switch (SortColumn)
            {
                case TreeColumn.Name: r = Util.NaturalCompare(a.Name, b.Name); break;
                case TreeColumn.Allocated: r = a.Allocated.CompareTo(b.Allocated); break;
                case TreeColumn.Files: r = a.Files.CompareTo(b.Files); break;
                case TreeColumn.Folders: r = a.Folders.CompareTo(b.Folders); break;
                case TreeColumn.LastChange: r = a.LastWriteUtc.CompareTo(b.LastWriteUtc); break;
                case TreeColumn.Owner:
                    r = string.Compare(a.Owner ?? "", b.Owner ?? "", StringComparison.CurrentCultureIgnoreCase);
                    break;
                case TreeColumn.Percent:
                default: r = a.Size.CompareTo(b.Size); break;
            }
            if (r == 0) r = Util.NaturalCompare(a.Name, b.Name);
            return SortAscending ? r : -r;
        }

        public void SetSort(int column, bool ascending)
        {
            SortColumn = column;
            SortAscending = ascending;
            _header.Invalidate();
            SortModel();
            Reload();
        }

        public FsNode SelectedFsNode => _tree.SelectedNode?.Tag as FsNode;

        // the gold chain runs from the root down to the folder you are in,
        // picking a file keeps the gold on its folder
        internal void UpdateChain()
        {
            var sel = SelectedFsNode ?? RootFs;
            var oldChain = _chain;
            var oldSel = _selected;

            var folder = sel;
            while (folder != null && !folder.IsDirectory) folder = folder.Parent;

            var newChain = new HashSet<FsNode>();
            for (var p = folder; p != null; p = p.Parent) newChain.Add(p);

            _chain = newChain;
            _selected = sel;

            if (oldSel != null) InvalidateRow(oldSel);
            if (sel != null) InvalidateRow(sel);
            foreach (var n in oldChain) InvalidateRow(n);
            foreach (var n in newChain) InvalidateRow(n);

            _breadcrumb.SetPath(sel);
        }

        internal BreadcrumbBar Breadcrumb => _breadcrumb;

        public void SelectNode(FsNode fs)
        {
            if (fs == null || RootFs == null) return;
            // rows only exist along expanded branches, so open the chain on
            // the way down (breadcrumb jumps land here with collapsed parts)
            var chain = new List<FsNode> { RootFs };
            for (var n = fs.Parent; n != null; n = n.Parent) chain.Add(n);
            chain.Reverse();
            foreach (var n in chain)
            {
                if (!_map.TryGetValue(n, out TreeNode tn)) return;
                if (tn.Nodes.Count == 1 && tn.Nodes[0].Tag == null)
                    PopulateNode(tn, n);
                if (n != fs) tn.Expand();
            }
            if (_map.TryGetValue(fs, out TreeNode target))
            {
                _tree.SelectedNode = target;
                target.EnsureVisible();
                // a programmatic selection does not raise AfterSelect on
                // every runtime, so keep breadcrumb and listeners current
                // from here as well
                UpdateChain();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RemoveNode(FsNode fs)
        {
            if (_map.TryGetValue(fs, out TreeNode tn))
            {
                _map.Remove(fs);
                tn.Remove();
            }
            // drop the subtree's rows from the bookkeeping too
            foreach (var n in fs.EnumerateAll())
            {
                _map.Remove(n);
                _populated.Remove(n);
            }
            RefreshAncestors(fs);
        }

        public void RefreshNode(FsNode fs)
        {
            InvalidateRow(fs);
            RefreshAncestors(fs);
        }

        void RefreshAncestors(FsNode fs)
        {
            foreach (var a in fs.Ancestors()) InvalidateRow(a);
        }

        void InvalidateRow(FsNode fs)
        {
            if (fs != null && _map.TryGetValue(fs, out TreeNode tn))
            {
                try
                {
                    var b = tn.Bounds;
                    _tree.Invalidate(new Rectangle(0, b.Y, _tree.Width, ItemHeight()));
                }
                catch { }
            }
        }

        int ItemHeight() => Font.Height + 8;

        public void RefreshAll() => _tree.Invalidate();

        public void ExpandBelow(FsNode fs)
        {
            if (fs == null || !_map.TryGetValue(fs, out TreeNode tn)) return;
            _tree.BeginUpdate();
            ExpandRecursive(tn, fs);
            _tree.EndUpdate();
        }

        void ExpandRecursive(TreeNode tn, FsNode fs)
        {
            if (!fs.HasChildren) return;
            if (tn.Nodes.Count == 1 && tn.Nodes[0].Tag == null) tn.Nodes.RemoveAt(0);
            PopulateNode(tn, fs);
            tn.Expand();
            foreach (var c in fs.Children)
                if (_map.TryGetValue(c, out TreeNode ctn))
                    ExpandRecursive(ctn, c);
        }

        public void CollapseBelow(FsNode fs)
        {
            if (fs != null && _map.TryGetValue(fs, out TreeNode tn)) tn.Collapse(false);
        }

        public void ExpandAll()
        {
            if (RootFs != null) ExpandBelow(RootFs);
        }

        public void CollapseAll()
        {
            if (RootFs != null) CollapseBelow(RootFs);
        }

        // --- cell text ---

        internal string CellText(FsNode fs, int col)
        {
            switch (col)
            {
                case TreeColumn.Name: return fs.Name;
                case TreeColumn.Size: return Util.FormatBytes(fs.Size, Unit);
                case TreeColumn.Allocated: return Util.FormatBytes(fs.Allocated, Unit);
                case TreeColumn.Files: return fs.IsDirectory ? fs.Files.ToString("N0") : "";
                case TreeColumn.Folders: return fs.IsDirectory ? fs.Folders.ToString("N0") : "";
                case TreeColumn.Percent:
                    return fs.PercentOfParent().ToString("0.#") + " %";
                case TreeColumn.LastChange:
                    return fs.LastWriteUtc == DateTime.MinValue ? "" : fs.LastWriteUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                case TreeColumn.Owner: return fs.Owner ?? "";
            }
            return "";
        }

        // called by the header while a column is being dragged wider/narrower
        internal void ColumnWidthLiveChanged()
        {
            RecalcColumns();
        }

        internal void ColumnVisibilityChanged()
        {
            RecalcColumns();
            Reload();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateSpaceWidth();
            RecalcColumns();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            StartLayoutTimer();
        }

        // --- drawing entry points used by the child controls ---

        // pens and brushes live as long as the app, drawing runs per row
        static readonly Brush s_rowBrush = new SolidBrush(Color.White);
        static readonly Brush s_goldBrush = new SolidBrush(Color.FromArgb(255, 226, 132));
        static readonly Pen s_glyphBorder = new Pen(Color.FromArgb(120, 120, 120));
        static readonly Pen s_glyphMark = new Pen(Color.FromArgb(70, 70, 70));
        static readonly Pen s_guidePen = new Pen(Color.FromArgb(150, 190, 190, 200))
        { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
        static readonly Pen s_selectionBorder = new Pen(Color.FromArgb(68, 114, 196), 2);

        internal void DrawRow(Graphics g, TreeNode node, Rectangle bounds, TreeNodeStates state, bool focused)
        {
            var fs = (FsNode)node.Tag;
            int h = ItemHeight();
            int y = bounds.Y;
            int rowW = Math.Max(bounds.Width, _tree.ClientSize.Width);

            bool selected = (state & TreeNodeStates.Selected) != 0;
            bool inChain = _chain.Contains(fs);

            // rows on the open path from the root down to the selected folder
            // are gold, that is what makes the current folder obvious
            Color back = inChain ? Color.FromArgb(255, 226, 132)
                : selected ? (focused ? SystemColors.Highlight : SystemColors.ControlLight)
                : Color.White;
            g.FillRectangle(s_rowBrush, bounds.X, y, rowW, h);
            if (inChain)
                g.FillRectangle(s_goldBrush, bounds.X, y, rowW, h);

            int scrollX = _tree.GetScrollX();
            int gx = bounds.X + 2;

            // dotted guides, one per ancestor level, they show what belongs
            // to which folder
            for (int l = 0; l < node.Level; l++)
            {
                int lineX = 2 - scrollX + l * _tree.Indent + 8;
                if (lineX < gx) g.DrawLine(s_guidePen, lineX, y + 2, lineX, y + h - 2);
            }

            // expand glyph
            if (fs.HasChildren)
            {
                var r = new Rectangle(gx, y + (h - 12) / 2, 12, 12);
                g.FillRectangle(SystemBrushes.Window, r);
                g.DrawRectangle(s_glyphBorder, r);
                int my = y + h / 2;
                g.DrawLine(s_glyphMark, r.Left + 3, my, r.Right - 3, my);
                if (!node.IsExpanded)
                    g.DrawLine(s_glyphMark, gx + 6, r.Top + 3, gx + 6, r.Bottom - 3);
            }

            int ix = gx + 15;
            DrawIcon(g, fs, node.IsExpanded, ix, y + (h - 16) / 2);

            int lx = ix + 19;

            // name column, clipped so long names stop at the column edge
            int nameRight = _colTotal - scrollX;
            foreach (var cp in _colPos)
                if (cp.Key == TreeColumn.Name) { nameRight = cp.Value + Columns[cp.Key].Width - scrollX; break; }
            var labelRect = new Rectangle(lx, y, Math.Max(0, nameRight - lx), h);
            if (labelRect.Width > 0)
            {
                var clip = g.Clip;
                g.SetClip(labelRect);
                Color textColor = inChain || !selected || !focused
                    ? (fs.HasError ? Color.Firebrick : SystemColors.ControlText)
                    : SystemColors.HighlightText;
                string label = fs.Name + (fs.HasError ? "  <access denied>" : "");
                TextRenderer.DrawText(g, label, Font, labelRect, textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                g.Clip = clip;
            }

            // the other columns sit at fixed positions
            foreach (var cp in _colPos)
            {
                int ci = cp.Key;
                if (ci == TreeColumn.Name) continue;
                int cx = cp.Value - scrollX;
                int w = Columns[ci].Width;
                if (cx + w < 0 || cx > _tree.ClientSize.Width) continue;
                var cell = new Rectangle(cx, y, w, h);

                if (ShowBars && ci == TreeColumn.Size)
                {
                    double frac = fs.Parent == null ? 1.0
                        : (fs.Parent.Size > 0 ? (double)fs.Size / fs.Parent.Size : 0.0);
                    int barW = (int)((w - 6) * Math.Min(1.0, frac));
                    if (barW > 0)
                    {
                        using (var b = new SolidBrush(BarColor))
                            g.FillRectangle(b, cx + 2, y + 2, barW, h - 4);
                    }
                }

                if (ShowBars && ci == TreeColumn.Percent)
                {
                    double pct = fs.PercentOfParent();
                    int barW = (int)((w - 6) * Math.Min(1.0, pct / 100.0));
                    if (barW > 0)
                    {
                        using (var b = new SolidBrush(Color.FromArgb(150, 197, 189, 243)))
                            g.FillRectangle(b, cx + 2, y + 2, barW, h - 4);
                    }
                }

                Color c = !inChain && selected && focused ? SystemColors.HighlightText : SystemColors.ControlText;
                var flags = (Columns[ci].Align == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left)
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                var textRect = new Rectangle(cx + 4, y, w - 8, h);
                TextRenderer.DrawText(g, CellText(fs, ci), Font, textRect, c, flags);
            }

            // blue frame around the selected row of the open path
            if (selected && inChain)
                g.DrawRectangle(s_selectionBorder, 0, y + 1, rowW - 2, h - 2);
        }

        void DrawIcon(Graphics g, FsNode fs, bool expanded, int x, int y)
        {
            if (fs.IsDirectory)
            {
                // simple folder, open variant when expanded
                var body = new Rectangle(x, y + 3, 14, 10);
                var tab = new Rectangle(x, y + 1, 7, 4);
                Color face = expanded ? Color.FromArgb(252, 224, 148) : Color.FromArgb(250, 214, 110);
                using (var b = new SolidBrush(face))
                {
                    g.FillRectangle(b, tab);
                    g.FillRectangle(b, body);
                }
                using (var p = new Pen(Color.FromArgb(150, 120, 60)))
                {
                    g.DrawRectangle(p, tab);
                    g.DrawRectangle(p, body);
                }
                if (fs.IsReparsePoint)
                {
                    // little arrow, Explorer marks shortcuts the same way
                    using (var p = new Pen(Color.FromArgb(40, 90, 200), 2f))
                    {
                        g.DrawLine(p, x + 6, y + 11, x + 12, y + 5);
                        g.DrawLine(p, x + 12, y + 5, x + 8, y + 5);
                        g.DrawLine(p, x + 12, y + 5, x + 12, y + 9);
                    }
                }
            }
            else
            {
                var sheet = new Rectangle(x + 2, y, 11, 15);
                using (var b = new SolidBrush(Color.White))
                    g.FillRectangle(b, sheet);
                using (var p = new Pen(Color.FromArgb(130, 130, 130)))
                {
                    g.DrawRectangle(p, sheet);
                    using (var pl = new Pen(Color.FromArgb(170, 190, 210)))
                    {
                        g.DrawLine(pl, x + 4, y + 4, x + 11, y + 4);
                        g.DrawLine(pl, x + 4, y + 7, x + 11, y + 7);
                        g.DrawLine(pl, x + 4, y + 10, x + 9, y + 10);
                    }
                }
            }
        }

        // --- nested: the actual TreeView ---

        class ColumnTree : TreeView
        {
            readonly TreeListView _owner;

            public ColumnTree(TreeListView owner)
            {
                _owner = owner;
                DrawMode = TreeViewDrawMode.OwnerDrawAll;
                DoubleBuffered = true;
                HideSelection = false;
                ShowLines = false;
                ShowPlusMinus = false;
                ShowRootLines = false;
                FullRowSelect = true;
                Indent = 18;
                BorderStyle = BorderStyle.None;
                ItemHeight = owner.ItemHeight();
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                ItemHeight = _owner.ItemHeight();
            }

            protected override void OnDrawNode(DrawTreeNodeEventArgs e)
            {
                if (e.Node.Tag is FsNode)
                    _owner.DrawRow(e.Graphics, e.Node, e.Bounds, e.State, Focused);
                else
                    base.OnDrawNode(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                var n = GetNodeAt(e.X, e.Y);
                if (n == null || !(n.Tag is FsNode)) return;
                try
                {
                    var b = n.Bounds;
                    var r = new Rectangle(b.X + 2, b.Y + (ItemHeight - 12) / 2, 12, 12);
                    if (r.Contains(e.X, e.Y)) n.Toggle();
                }
                catch { }
            }

            protected override void OnBeforeExpand(TreeViewCancelEventArgs e)
            {
                base.OnBeforeExpand(e);
                // first time this is opened: swap the placeholder for real rows
                if (e.Node?.Tag is FsNode fs && e.Node.Nodes.Count == 1 && e.Node.Nodes[0].Tag == null)
                {
                    e.Node.Nodes.RemoveAt(0);
                    _owner.PopulateNode(e.Node, fs);
                }
            }

            protected override void OnAfterSelect(TreeViewEventArgs e)
            {
                base.OnAfterSelect(e);
                _owner.UpdateChain();
                _owner.SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
            {
                base.OnNodeMouseClick(e);
                // right click should make the row under the mouse the
                // selected one before any menu shows up
                if (e.Button == MouseButtons.Right)
                    SelectedNode = e.Node;
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && SelectedNode != null)
                {
                    _owner.NodeActivated?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Delete && SelectedNode != null)
                {
                    _owner.DeleteRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
                base.OnKeyDown(e);
            }

            protected override void OnNodeMouseDoubleClick(TreeNodeMouseClickEventArgs e)
            {
                base.OnNodeMouseDoubleClick(e);
                // folders toggle on double click all by themselves, files open
                if (e.Node?.Tag is FsNode fs && !fs.IsDirectory)
                    Util.ShellOpen(fs.FullName);
            }

            internal int GetScrollX()
            {
                if (!IsHandleCreated) return 0;
                var si = new SCROLLINFO { cbSize = Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_POS };
                return GetScrollInfo(Handle, SB_HORZ, ref si) != 0 ? si.nPos : 0;
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == WM_HSCROLL)
                    _owner._header.Invalidate();
            }

            const int WM_HSCROLL = 0x114;
            const int SB_HORZ = 0;
            const int SIF_POS = 0x4;

            [StructLayout(LayoutKind.Sequential)]
            struct SCROLLINFO
            {
                public int cbSize;
                public uint fMask;
                public int nMin;
                public int nMax;
                public int nPage;
                public int nPos;
                public int nTrackPos;
            }

            [DllImport("user32.dll")]
            static extern int GetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO si);
        }

        // --- nested: column header ---

        class HeaderStrip : Control
        {
            readonly TreeListView _owner;
            int _dragCol = -1;
            int _dragStartX, _dragStartW;
            int _pressCol = -1, _pressX;
            bool _dragging;

            public HeaderStrip(TreeListView owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                         ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Cursor = Cursors.Default;
                Height = owner.Font.Height + 11;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                int scrollX = _owner._tree.GetScrollX();
                using (var bg = new SolidBrush(SystemColors.Control))
                    g.FillRectangle(bg, ClientRectangle);
                using (var dark = new Pen(SystemColors.ControlDark))
                using (var text = new SolidBrush(SystemColors.ControlText))
                {
                    foreach (var cp in _owner._colPos)
                    {
                        var col = _owner.Columns[cp.Key];
                        int x = cp.Value - scrollX;
                        if (x + col.Width < 0 || x > Width) continue;

                        bool sorted = cp.Key == _owner.SortColumn;
                        var textRect = new Rectangle(x + 4, 0, col.Width - (sorted ? 18 : 8), Height - 1);
                        var flags = (col.Align == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left)
                            | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                        TextRenderer.DrawText(g, col.Title, Font, textRect, SystemColors.ControlText, flags);

                        if (sorted)
                        {
                            int ax = x + col.Width - 11;
                            int ay = Height / 2;
                            Point[] tri = _owner.SortAscending
                                ? new[] { new Point(ax, ay + 3), new Point(ax + 8, ay + 3), new Point(ax + 4, ay - 4) }
                                : new[] { new Point(ax, ay - 3), new Point(ax + 8, ay - 3), new Point(ax + 4, ay + 4) };
                            g.FillPolygon(SystemBrushes.ControlText, tri);
                        }

                        g.DrawLine(dark, x + col.Width - 1, 3, x + col.Width - 1, Height - 5);
                    }
                }
                using (var p = new Pen(SystemColors.ControlDark))
                    g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }

            int DividerAt(int x)
            {
                int scrollX = _owner._tree.GetScrollX();
                foreach (var cp in _owner._colPos)
                {
                    int edge = cp.Value + _owner.Columns[cp.Key].Width - scrollX;
                    if (Math.Abs(x - edge) <= 3) return cp.Key;
                }
                return -1;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Right)
                {
                    _owner.HeaderRightClicked?.Invoke(this, EventArgs.Empty);
                    return;
                }
                if (e.Button != MouseButtons.Left) return;
                int col = DividerAt(e.X);
                if (col >= 0)
                {
                    _dragCol = col;
                    _dragStartX = e.X;
                    _dragStartW = _owner.Columns[col].Width;
                    _dragging = false;
                }
                else
                {
                    _pressCol = ColumnAt(e.X);
                    _pressX = e.X;
                }
                Capture = true;
            }

            int ColumnAt(int x)
            {
                int scrollX = _owner._tree.GetScrollX();
                foreach (var cp in _owner._colPos)
                {
                    int cx = cp.Value - scrollX;
                    if (x >= cx && x < cx + _owner.Columns[cp.Key].Width) return cp.Key;
                }
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (_dragCol >= 0)
                {
                    int dx = e.X - _dragStartX;
                    if (!_dragging && Math.Abs(dx) > 2) _dragging = true;
                    if (_dragging)
                    {
                        var col = _owner.Columns[_dragCol];
                        col.Width = Math.Max(col.MinWidth, _dragStartW + dx);
                        _owner.ColumnWidthLiveChanged();
                    }
                    return;
                }
                Cursor = DividerAt(e.X) >= 0 ? Cursors.VSplit : Cursors.Default;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (_dragCol >= 0)
                {
                    if (_dragging) _owner.ColumnWidthLiveChanged();
                    _dragCol = -1;
                    _dragging = false;
                    Capture = false;
                    return;
                }
                if (_pressCol >= 0 && Math.Abs(e.X - _pressX) <= 4)
                    _owner.ColumnClicked?.Invoke(this, new TreeColumnClickEventArgs(_pressCol));
                _pressCol = -1;
                Capture = false;
            }

            protected override void OnMouseCaptureChanged(EventArgs e)
            {
                base.OnMouseCaptureChanged(e);
                if (_dragCol >= 0 && !Capture)
                {
                    _dragCol = -1;
                    _dragging = false;
                }
            }
        }
    }
}
