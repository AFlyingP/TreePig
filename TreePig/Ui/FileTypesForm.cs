using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TreePig.Core;

namespace TreePig.Ui
{
    // how much room each file extension eats below the scanned root
    class FileTypesForm : Form
    {
        public FileTypesForm(FsNode root)
        {
            Text = "File types";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(620, 560);

            var types = new Dictionary<string, long[]>();
            long totalSize = 0, totalFiles = 0;

            if (root != null)
            {
                foreach (var n in root.EnumerateAll())
                {
                    if (n.IsDirectory) continue;
                    string ext;
                    try { ext = Path.GetExtension(n.Name); }
                    catch { ext = ""; }
                    ext = ext.Length == 0 ? "(none)" : ext.Substring(1).ToLowerInvariant();
                    if (!types.TryGetValue(ext, out var agg))
                        agg = new[] { 0L, 0L };
                    agg[0] += n.Size;   // size
                    agg[1] += 1;        // count
                    types[ext] = agg;
                    totalSize += n.Size;
                    totalFiles += 1;
                }
            }

            var entries = new List<KeyValuePair<string, long[]>>(types);
            entries.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));

            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false
            };
            list.Columns.Add("Extension", 110, HorizontalAlignment.Left);
            list.Columns.Add("Files", 90, HorizontalAlignment.Right);
            list.Columns.Add("Total Size", 120, HorizontalAlignment.Right);
            list.Columns.Add("Percent", 90, HorizontalAlignment.Right);

            list.BeginUpdate();
            foreach (var kv in entries)
            {
                var item = new ListViewItem(kv.Key);
                item.SubItems.Add(kv.Value[1].ToString("N0"));
                item.SubItems.Add(Util.FormatBytes(kv.Value[0]));
                item.SubItems.Add(totalSize > 0 ? (100.0 * kv.Value[0] / totalSize).ToString("0.0") + " %" : "");
                list.Items.Add(item);
            }
            list.EndUpdate();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 34 };
            var info = new Label
            {
                Text = string.Format("{0} files in {1} types, {2} total",
                    totalFiles.ToString("N0"), entries.Count.ToString("N0"), Util.FormatBytes(totalSize)),
                Location = new Point(10, 9),
                AutoSize = true,
                ForeColor = Color.DimGray
            };
            bottom.Controls.Add(info);

            Controls.Add(list);
            Controls.Add(bottom);
        }
    }
}
