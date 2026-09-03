using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TreePig.Core;

namespace TreePig.Ui
{
    // top N biggest files below the scanned root
    class LargestFilesForm : Form
    {
        readonly List<FsNode> _files = new List<FsNode>();
        readonly ListView _list;
        readonly NumericUpDown _count;

        // main form listens to this so its totals stay honest after a delete
        public event EventHandler FilesChanged;

        public LargestFilesForm(FsNode root)
        {
            Text = "Largest files";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(880, 540);

            if (root != null)
                foreach (var n in root.EnumerateAll())
                    if (!n.IsDirectory) _files.Add(n);
            _files.Sort((a, b) => b.Size.CompareTo(a.Size));

            var top = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 8, 8, 0) };
            var lbl1 = new Label { Text = "Show the", Location = new Point(8, 10), AutoSize = true };
            _count = new NumericUpDown
            {
                Location = new Point(66, 6),
                Minimum = 10,
                Maximum = 5000,
                Value = 100,
                Increment = 50,
                Width = 70
            };
            _count.ValueChanged += (s, e) => FillList();
            var lbl2 = new Label { Text = "biggest files", Location = new Point(142, 10), AutoSize = true };
            top.Controls.AddRange(new Control[] { lbl1, _count, lbl2 });

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false
            };
            _list.Columns.Add("Size", 110, HorizontalAlignment.Right);
            _list.Columns.Add("Name", 250, HorizontalAlignment.Left);
            _list.Columns.Add("Path", 350, HorizontalAlignment.Left);
            _list.Columns.Add("Last Modified", 140, HorizontalAlignment.Left);
            _list.DoubleClick += (s, e) => ShowSelected();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 42 };
            var btnShow = new Button { Text = "Show in Explorer", Location = new Point(8, 8), Size = new Size(120, 26) };
            btnShow.Click += (s, e) => ShowSelected();
            var btnCopy = new Button { Text = "Copy path", Location = new Point(134, 8), Size = new Size(90, 26) };
            btnCopy.Click += (s, e) =>
            {
                var fs = Selected();
                if (fs != null) Clipboard.SetText(fs.FullName);
            };
            var btnDelete = new Button { Text = "Delete", Location = new Point(230, 8), Size = new Size(80, 26) };
            btnDelete.Click += (s, e) => DeleteSelected();
            var btnClose = new Button { Text = "Close", Location = new Point(780, 8), Size = new Size(80, 26), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            btnClose.Click += (s, e) => Close();
            bottom.Controls.AddRange(new Control[] { btnShow, btnCopy, btnDelete, btnClose });

            Controls.Add(_list);
            Controls.Add(top);
            Controls.Add(bottom);

            FillList();
        }

        FsNode Selected()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as FsNode;
        }

        void FillList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            int n = Math.Min((int)_count.Value, _files.Count);
            for (int i = 0; i < n; i++)
            {
                var fs = _files[i];
                var item = new ListViewItem(Util.FormatBytes(fs.Size)) { Tag = fs };
                item.SubItems.Add(fs.Name);
                item.SubItems.Add(fs.FullName);
                item.SubItems.Add(fs.LastWriteUtc == DateTime.MinValue ? "" : fs.LastWriteUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            Text = string.Format("Largest files ({0} of {1})", n.ToString("N0"), _files.Count.ToString("N0"));
        }

        void ShowSelected()
        {
            var fs = Selected();
            if (fs != null) Util.ShowInExplorer(fs.FullName);
        }

        void DeleteSelected()
        {
            var fs = Selected();
            if (fs == null) return;
            var answer = MessageBox.Show(this, string.Format("Send \"{0}\" to the recycle bin?", fs.Name),
                "TreePig", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            bool ok;
            try { ok = Util.DeleteToRecycleBin(fs.FullName); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "TreePig", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!ok)
            {
                MessageBox.Show(this, "Windows would not delete it.", "TreePig",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            fs.RemoveFromTree();
            _files.Remove(fs);
            FillList();
            FilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
