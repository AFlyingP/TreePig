using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TreePig.Core;

namespace TreePig.Ui
{
    class MainForm : Form
    {
        readonly TreeListView _tree;
        readonly MenuStrip _menu;
        readonly ToolStrip _toolbar;
        readonly StatusStrip _status;
        readonly ToolStripStatusLabel _statusInfo;
        readonly ToolStripStatusLabel _statusSel;
        readonly ToolStripStatusLabel _statusErrors;
        readonly Label _emptyHint;

        ToolStripMenuItem _miRescan, _miCancel, _miExport, _miLargest, _miFileTypes;
        ToolStripButton _btnRescan, _btnStop;

        readonly AppSettings _settings = AppSettings.Load();
        Scanner _scanner;
        CancellationTokenSource _cts;
        ScanProgressDialog _scanDlg;
        LargestFilesForm _largestForm;
        FileTypesForm _typesForm;
        long _totalErrors;
        bool _scanning;
        string _pendingScan;
        string _lastPath;

        public MainForm(string[] args)
        {
            Text = "TreePig";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1000, 660);
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _tree = new TreeListView
            {
                Dock = DockStyle.Fill,
                Unit = Util.ParseUnit(_settings.Unit),
                ShowBars = _settings.ShowBars,
                BarColor = Util.ParseColor(_settings.BarColor, Color.FromArgb(110, 192, 80, 77)),
                SortColumn = _settings.SortColumn,
                SortAscending = _settings.SortAscending
            };
            ApplyColumnSettings(_tree);
            _tree.ColumnClicked += TreeColumnClicked;
            _tree.SelectionChanged += TreeSelectionChanged;
            _tree.NodeActivated += (s, e) => OpenSelection();
            _tree.DeleteRequested += (s, e) => DeleteSelection();
            _tree.ContextMenuStrip = BuildContextMenu();

            _emptyHint = new Label
            {
                Text = "Open a folder to scan it (Ctrl+O), or drop one here.",
                ForeColor = Color.Gray,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _menu = BuildMenu();
            _toolbar = BuildToolbar();
            _status = BuildStatus(out _statusInfo, out _statusSel, out _statusErrors);

            Controls.Add(_tree);
            _tree.Controls.Add(_emptyHint);
            Controls.Add(_status);
            Controls.Add(_toolbar);
            Controls.Add(_menu);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            FormClosing += (s, e) =>
            {
                _cts?.Cancel();
                CloseScanDialog();
            };

            if (args != null && args.Length > 0 && (Directory.Exists(args[0]) || File.Exists(args[0])))
                _pendingScan = args[0];
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RestoreWindow();
            if (_pendingScan != null)
                StartScan(_pendingScan, false);
            else if (_settings.ScanLastOnStart && Directory.Exists(_settings.LastPath))
                StartScan(_settings.LastPath, false);
            PositionHint();
        }

        void RestoreWindow()
        {
            var r = _settings.WindowRect;
            if (r != null && r.Length == 4 && r[2] >= 400 && r[3] >= 300)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(r[0], r[1], r[2], r[3]);
            }
            if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
        }

        void ApplyColumnSettings(TreeListView tree)
        {
            var w = _settings.ColumnWidths;
            var v = _settings.ColumnVisible;
            for (int i = 0; i < tree.Columns.Count; i++)
            {
                if (w != null && w.Length == tree.Columns.Count)
                    tree.Columns[i].Width = Math.Max(tree.Columns[i].MinWidth, w[i]);
                if (v != null && v.Length == tree.Columns.Count)
                    tree.Columns[i].Visible = v[i];
            }
            tree.ColumnVisibilityChanged();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionHint();
        }

        void PositionHint()
        {
            if (_tree == null || _emptyHint == null) return;
            _emptyHint.Location = new Point(
                (_tree.ClientSize.Width - _emptyHint.PreferredWidth) / 2,
                (_tree.ClientSize.Height - _emptyHint.PreferredHeight) / 2);
        }

        // --- menus / toolbar ---

        MenuStrip BuildMenu()
        {
            var menu = new MenuStrip();

            var file = new ToolStripMenuItem("&File");
            var miScan = new ToolStripMenuItem("Scan &Folder...", null, (s, e) => ScanFolder()) { ShortcutKeys = Keys.Control | Keys.O };
            var miAdd = new ToolStripMenuItem("&Add Folder to Scan...", null, (s, e) => ScanFolder(addMode: true));
            _miRescan = new ToolStripMenuItem("&Rescan", null, (s, e) => Rescan()) { ShortcutKeys = Keys.F5 };
            _miCancel = new ToolStripMenuItem("&Cancel Scan", null, (s, e) => CancelScan());
            _miExport = new ToolStripMenuItem("Export as &CSV...", null, (s, e) => ExportCsv());
            var miClipboard = new ToolStripMenuItem("Copy &Tree to Clipboard", null, (s, e) => CopyTreeToClipboard()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.C };
            var miExit = new ToolStripMenuItem("E&xit", null, (s, e) => Close());

            file.DropDownItems.Add(miScan);
            file.DropDownItems.Add(miAdd);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(_miRescan);
            file.DropDownItems.Add(_miCancel);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(_miExport);
            file.DropDownItems.Add(miClipboard);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(miExit);
            file.DropDownOpening += (s, e) => RefreshDriveItems(file.DropDownItems);

            var view = new ToolStripMenuItem("&View");
            var miExpand = new ToolStripMenuItem("Expand &All", null, (s, e) => _tree.ExpandAll());
            var miCollapse = new ToolStripMenuItem("C&ollapse All", null, (s, e) => _tree.CollapseAll());
            view.DropDownItems.Add(miExpand);
            view.DropDownItems.Add(miCollapse);
            view.DropDownItems.Add(new ToolStripSeparator());

            var units = new ToolStripMenuItem("&Units");
            foreach (SizeUnit u in Enum.GetValues(typeof(SizeUnit)))
            {
                var item = new ToolStripMenuItem(u.ToString()) { Tag = u, Checked = _tree.Unit == u };
                item.Click += (s, e) =>
                {
                    _tree.Unit = u;
                    foreach (ToolStripMenuItem m in units.DropDownItems)
                        m.Checked = ReferenceEquals(m, item);
                    _tree.RefreshAll();
                };
                units.DropDownItems.Add(item);
            }
            view.DropDownItems.Add(units);

            view.DropDownItems.Add(new ToolStripSeparator());
            foreach (var col in new[]
            {
                new { Id = TreeColumn.Allocated, Title = "Show &Allocated" },
                new { Id = TreeColumn.Files, Title = "Show &Files" },
                new { Id = TreeColumn.Folders, Title = "Show F&olders" },
                new { Id = TreeColumn.Percent, Title = "Show &Percent" },
                new { Id = TreeColumn.LastChange, Title = "Show &Last Change" },
                new { Id = TreeColumn.Owner, Title = "Show O&wner" },
            })
            {
                var item = new ToolStripMenuItem(col.Title) { Checked = _tree.Columns[col.Id].Visible, Tag = col.Id };
                item.Click += (s, e) =>
                {
                    item.Checked = !item.Checked;
                    _tree.Columns[col.Id].Visible = item.Checked;
                    _tree.ColumnVisibilityChanged();
                };
                view.DropDownItems.Add(item);
            }

            var miBars = new ToolStripMenuItem("Show Size &Bars") { Checked = _tree.ShowBars };
            miBars.Click += (s, e) =>
            {
                miBars.Checked = !miBars.Checked;
                _tree.ShowBars = miBars.Checked;
                _tree.RefreshAll();
            };
            view.DropDownItems.Add(miBars);
            view.DropDownItems.Add(new ToolStripSeparator());

            _miLargest = new ToolStripMenuItem("&Largest Files...", null, (s, e) => ShowLargestFiles()) { ShortcutKeys = Keys.Control | Keys.L };
            _miFileTypes = new ToolStripMenuItem("File &Types...", null, (s, e) => ShowFileTypes()) { ShortcutKeys = Keys.Control | Keys.T };
            view.DropDownItems.Add(_miLargest);
            view.DropDownItems.Add(_miFileTypes);

            var tools = new ToolStripMenuItem("&Tools");
            tools.DropDownItems.Add(new ToolStripMenuItem("&Options...", null, (s, e) => ShowOptions()));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(new ToolStripMenuItem("&About TreePig...", null, (s, e) => new AboutForm().ShowDialog(this)));

            menu.Items.AddRange(new ToolStripItem[] { file, view, tools, help });
            return menu;
        }

        // one entry per ready drive, like the classic tree size tools
        void RefreshDriveItems(ToolStripItemCollection items)
        {
            // strip entries from an earlier refresh first
            while (items.Count > 0)
            {
                var last = items[items.Count - 1];
                if (last is ToolStripSeparator || last.Tag is string)
                    items.RemoveAt(items.Count - 1);
                else
                    break;
            }

            var drives = new List<ToolStripItem>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.CDRom || !d.IsReady) continue;
                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;
                    var item = new ToolStripMenuItem($"{label} ({d.Name.TrimEnd('\\', ':')}:)  ") { Tag = d.Name };
                    item.Click += (s, e) => StartScan(d.Name, false);
                    drives.Add(item);
                }
            }
            catch { }
            if (drives.Count > 0)
            {
                drives.Insert(0, new ToolStripSeparator());
                items.AddRange(drives.ToArray());
            }
        }

        ToolStrip BuildToolbar()
        {
            var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };

            var btnScan = new ToolStripButton("Scan folder");
            btnScan.Click += (s, e) => ScanFolder();

            _btnRescan = new ToolStripButton("Rescan") { Enabled = false };
            _btnRescan.Click += (s, e) => Rescan();

            _btnStop = new ToolStripButton("Stop") { Enabled = false };
            _btnStop.Click += (s, e) => CancelScan();

            var sep1 = new ToolStripSeparator();

            var btnLargest = new ToolStripButton("Largest files") { Enabled = false };
            btnLargest.Click += (s, e) => ShowLargestFiles();

            var btnTypes = new ToolStripButton("File types") { Enabled = false };
            btnTypes.Click += (s, e) => ShowFileTypes();

            var btnOptions = new ToolStripButton("Options");
            btnOptions.Click += (s, e) => ShowOptions();

            bar.Items.AddRange(new ToolStripItem[] { btnScan, _btnRescan, _btnStop, sep1, btnLargest, btnTypes, new ToolStripSeparator(), btnOptions });
            _toolbarButtons = new[] { btnLargest, btnTypes };
            return bar;
        }

        ToolStripButton[] _toolbarButtons;

        StatusStrip BuildStatus(out ToolStripStatusLabel info, out ToolStripStatusLabel sel, out ToolStripStatusLabel errors)
        {
            var strip = new StatusStrip { SizingGrip = true };
            info = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            sel = new ToolStripStatusLabel("") { AutoSize = true };
            errors = new ToolStripStatusLabel("") { AutoSize = true };
            strip.Items.AddRange(new ToolStripItem[] { info, sel, errors });
            return strip;
        }

        // --- scanning ---

        void ScanFolder(bool addMode = false)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = addMode ? "Pick a folder to add to the scan" : "Pick a folder or drive to scan",
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            StartScan(dlg.SelectedPath, addMode);
        }

        void StartScan(string path, bool addMode)
        {
            if (_scanning || string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) path = Path.GetDirectoryName(path);
                path = Path.GetFullPath(path);
            }
            catch { return; }

            _lastPath = path;
            _settings.LastPath = path;
            _cts = new CancellationTokenSource();
            _scanner = new Scanner(path, new ScanOptions { CollectOwner = _settings.CollectOwner });
            _scanning = true;
            SetBusyState(true);
            ShowScanDialog();

            var progress = new Progress<ScanProgress>(OnScanProgress);
            var token = _cts.Token;
            var scanner = _scanner;
            var add = addMode;

            Task.Run(async () =>
            {
                try
                {
                    var root = await scanner.ScanAsync(progress, token);
                    Ui(() => AttachResult(root, add, false));
                }
                catch (OperationCanceledException)
                {
                    Ui(() => AttachResult(scanner.Root, add, true));
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    Ui(() =>
                    {
                        _scanning = false;
                        SetBusyState(false);
                        CloseScanDialog();
                        MessageBox.Show(this, msg, "TreePig", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            });
        }

        // posts to the UI thread, quietly dropped when the window is gone
        void Ui(Action action)
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(action);
        }

        void AttachResult(FsNode root, bool addMode, bool canceled)
        {
            _scanning = false;
            CloseScanDialog();
            if (root == null)
            {
                SetBusyState(false);
                return;
            }

            if (addMode)
            {
                var current = _tree.RootFs;
                if (current == null)
                {
                    _tree.SetRoot(root);
                }
                else
                {
                    if (!current.IsVirtualRoot)
                    {
                        var virt = new FsNode
                        {
                            Name = "(scanned folders)",
                            FullName = "",
                            IsDirectory = true,
                            IsVirtualRoot = true
                        };
                        virt.AddDirChild(current);
                        virt.RollUp(current);
                        current = virt;
                    }
                    current.AddDirChild(root);
                    current.RollUp(root);
                    _tree.SetRoot(current);
                }
            }
            else
            {
                _tree.SetRoot(root);
            }

            _emptyHint.Visible = false;
            Text = root.IsVirtualRoot ? "TreePig" : "TreePig - " + root.FullName;
            UpdateSummary(canceled ? "  (cancelled)" : "");
            _totalErrors = _scanner.ErrorCount;
            SetErrorCount(_totalErrors);
            SetBusyState(false);
        }

        void UpdateSummary(string suffix = "")
        {
            var root = _tree.RootFs;
            if (root == null) return;
            _statusInfo.Text = string.Format("Scanned {0}: {1}, {2} files, {3} folders{4}",
                root.IsVirtualRoot ? "multiple folders" : root.FullName,
                Util.FormatBytes(root.Size),
                root.Files.ToString("N0"),
                root.Folders.ToString("N0"),
                suffix);
        }

        // errors inside one branch, the caller pays only for the part involved
        long CountErrorsBelow(FsNode node)
        {
            long n = 0;
            foreach (var x in node.EnumerateAll())
                if (x.HasError) n++;
            return n;
        }

        void Rescan()
        {
            var root = _tree.RootFs;
            if (root == null || _scanning) return;
            if (root.IsVirtualRoot)
            {
                // rebuild the whole multi scan from scratch
                var paths = new List<string>();
                foreach (var c in root.Children) paths.Add(c.FullName);
                if (paths.Count == 0) return;
                StartMultiScan(paths);
                return;
            }
            StartScan(root.FullName, false);
        }

        async void StartMultiScan(List<string> paths)
        {
            // scan one folder after another and collect them under a fresh
            // virtual root
            var virt = new FsNode
            {
                Name = "(scanned folders)",
                FullName = "",
                IsDirectory = true,
                IsVirtualRoot = true
            };
            _tree.SetRoot(virt);
            _emptyHint.Visible = false;
            _scanning = true;
            SetBusyState(true);

            foreach (var p in paths)
            {
                _cts = new CancellationTokenSource();
                var scanner = new Scanner(p, new ScanOptions { CollectOwner = _settings.CollectOwner });
                var progress = new Progress<ScanProgress>(OnScanProgress);
                try
                {
                    var root = await scanner.ScanAsync(progress, _cts.Token);
                    virt.AddDirChild(root);
                    virt.RollUp(root);
                    _tree.SetRoot(virt);
                    _totalErrors += scanner.ErrorCount;
                    SetErrorCount(_totalErrors);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
            _scanning = false;
            SetBusyState(false);
            Text = "TreePig";
        }

        void CancelScan()
        {
            if (_scanning) _cts?.Cancel();
        }

        void OnScanProgress(ScanProgress p)
        {
            if (!_scanning) return;
            _statusInfo.Text = string.Format("Scanning {0} ... {1}, {2} files, {3} folders, {4}",
                p.CurrentPath,
                Util.FormatBytes(p.Bytes),
                p.Files.ToString("N0"),
                p.Dirs.ToString("N0"),
                Util.FormatElapsed(p.Elapsed));
            _scanDlg?.UpdateProgress(p);
        }

        void ShowScanDialog()
        {
            if (_scanDlg != null) return;
            _scanDlg = new ScanProgressDialog(CancelScan);
            _scanDlg.Show(this);
        }

        void CloseScanDialog()
        {
            if (_scanDlg != null)
            {
                try { _scanDlg.Close(); } catch { }
                _scanDlg = null;
            }
        }

        void SetBusyState(bool busy)
        {
            _miRescan.Enabled = !busy;
            _miCancel.Enabled = busy;
            _miExport.Enabled = !busy;
            _btnRescan.Enabled = !busy && _tree.RootFs != null;
            _btnStop.Enabled = busy;
            _miLargest.Enabled = !busy && _tree.RootFs != null;
            _miFileTypes.Enabled = !busy && _tree.RootFs != null;
            _toolbarButtons[0].Enabled = _miLargest.Enabled;
            _toolbarButtons[1].Enabled = _miFileTypes.Enabled;
            if (busy) _statusErrors.Text = "";
            UseWaitCursor = false;
        }

        void SetErrorCount(long n)
        {
            _statusErrors.Text = n > 0 ? string.Format("{0} errors", n.ToString("N0")) : "";
            _statusErrors.ForeColor = n > 0 ? Color.Firebrick : SystemColors.ControlText;
        }

        // --- tree events ---

        void TreeColumnClicked(object sender, TreeColumnClickEventArgs e)
        {
            bool asc = _tree.SortColumn == e.Column ? !_tree.SortAscending : e.Column == TreeColumn.Name;
            _tree.SetSort(e.Column, asc);
        }

        void TreeSelectionChanged(object sender, EventArgs e)
        {
            var fs = _tree.SelectedFsNode;
            if (fs == null) { _statusSel.Text = ""; return; }
            string where = fs.IsDirectory ? "folder" : "file";
            _statusSel.Text = string.Format("{0}: {1}, {2:0.#}% of {3}",
                where,
                Util.FormatBytes(fs.Size),
                fs.PercentOfParent(),
                fs.Parent == null ? "total" : "parent");
        }

        // --- context menu ---

        ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var miOpen = new ToolStripMenuItem("Open", null, (s, e) => OpenSelection());
            var miShow = new ToolStripMenuItem("Show in Explorer", null, (s, e) =>
            {
                var fs = _tree.SelectedFsNode;
                if (fs != null) Util.ShowInExplorer(fs.FullName);
            });
            var miCmd = new ToolStripMenuItem("Open Command Prompt Here", null, (s, e) =>
            {
                var fs = _tree.SelectedFsNode;
                if (fs != null && fs.IsDirectory) Util.OpenCmdHere(fs.FullName);
            });

            var miCopyPath = new ToolStripMenuItem("Copy Full Path", null, (s, e) =>
            {
                var fs = _tree.SelectedFsNode;
                if (fs != null) Clipboard.SetText(fs.FullName);
            });
            var miCopyName = new ToolStripMenuItem("Copy Name", null, (s, e) =>
            {
                var fs = _tree.SelectedFsNode;
                if (fs != null) Clipboard.SetText(fs.Name);
            });

            var miRescanBranch = new ToolStripMenuItem("Rescan This Branch", null, (s, e) => RescanBranch(_tree.SelectedFsNode));
            var miDelete = new ToolStripMenuItem("Delete", null, (s, e) => DeleteSelection());
            var miExpand = new ToolStripMenuItem("Expand All Children", null, (s, e) => _tree.ExpandBelow(_tree.SelectedFsNode));
            var miCollapse = new ToolStripMenuItem("Collapse All Children", null, (s, e) => _tree.CollapseBelow(_tree.SelectedFsNode));

            menu.Items.AddRange(new ToolStripItem[]
            {
                miOpen, miShow, miCmd, new ToolStripSeparator(),
                miCopyPath, miCopyName, new ToolStripSeparator(),
                miRescanBranch, miDelete, new ToolStripSeparator(),
                miExpand, miCollapse
            });

            menu.Opening += (s, e) =>
            {
                var fs = _tree.SelectedFsNode;
                bool has = fs != null;
                miCmd.Enabled = has && fs.IsDirectory && !fs.IsVirtualRoot;
                miRescanBranch.Enabled = has && fs.IsDirectory && !fs.IsVirtualRoot && !_scanning;
                miDelete.Enabled = has && !fs.IsVirtualRoot;
                miExpand.Enabled = has && fs.IsDirectory;
                miCollapse.Enabled = has && fs.IsDirectory;
            };
            return menu;
        }

        void OpenSelection()
        {
            var fs = _tree.SelectedFsNode;
            if (fs == null || fs.IsVirtualRoot) return;
            try { Util.ShellOpen(fs.FullName); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "TreePig", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void DeleteSelection()
        {
            var fs = _tree.SelectedFsNode;
            if (fs == null || fs.IsVirtualRoot || _scanning) return;

            string what = fs.IsDirectory
                ? string.Format("folder \"{0}\" and everything inside it", fs.Name)
                : string.Format("file \"{0}\"", fs.Name);
            var answer = MessageBox.Show(this, "Send " + what + " to the recycle bin?",
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
            _totalErrors -= CountErrorsBelow(fs);
            SetErrorCount(_totalErrors);
            _tree.RemoveNode(fs);
            UpdateSummary();
        }

        async void RescanBranch(FsNode node)
        {
            if (node == null || _scanning || !node.IsDirectory || node.IsVirtualRoot) return;
            _scanning = true;
            SetBusyState(true);
            ShowScanDialog();

            long errorsBefore = CountErrorsBelow(node);
            _cts = new CancellationTokenSource();
            var scanner = new Scanner(node.FullName, new ScanOptions { CollectOwner = _settings.CollectOwner });
            var progress = new Progress<ScanProgress>(OnScanProgress);
            try
            {
                var fresh = await scanner.ScanAsync(progress, _cts.Token);
                var parent = node.Parent;
                if (parent == null)
                {
                    _tree.SetRoot(fresh);
                    _totalErrors = scanner.ErrorCount;
                }
                else
                {
                    parent.ReplaceChild(node, fresh);
                    _totalErrors += scanner.ErrorCount - errorsBefore;
                    _tree.Reload();
                }
                Text = _tree.RootFs.IsVirtualRoot ? "TreePig" : "TreePig - " + _tree.RootFs.FullName;
                UpdateSummary();
                SetErrorCount(_totalErrors);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "TreePig", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _scanning = false;
                SetBusyState(false);
                CloseScanDialog();
            }
        }

        // --- drag & drop ---

        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Link;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;
            var first = files[0];
            if (Directory.Exists(first))
            {
                if (files.Length == 1) StartScan(first, false);
                else StartMultiScan(new List<string>(files));
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Multiply) { _tree.ExpandAll(); return true; }
            if (keyData == Keys.Divide) { _tree.CollapseAll(); return true; }
            if (keyData == (Keys.Control | Keys.C))
            {
                var fs = _tree.SelectedFsNode;
                if (fs != null)
                {
                    Clipboard.SetText(fs.FullName);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // --- stubs filled in by later commits ---

        void ExportCsv()
        {
            var root = _tree.RootFs;
            if (root == null) return;
            using var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = SuggestFileName(root) + ".csv"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                File.WriteAllText(dlg.FileName, BuildTable(root, ','), Encoding.UTF8);
                _statusInfo.Text = "Exported to " + dlg.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "TreePig", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void CopyTreeToClipboard()
        {
            var root = _tree.RootFs;
            if (root == null) return;
            Clipboard.SetText(BuildTable(root, '\t'));
        }

        string SuggestFileName(FsNode root)
        {
            if (root.IsVirtualRoot) return "scan";
            var bad = Path.GetInvalidFileNameChars();
            var name = new string(root.Name.Select(c => Array.IndexOf(bad, c) >= 0 ? '_' : c).ToArray());
            return string.IsNullOrEmpty(name) ? "scan" : name;
        }

        string BuildTable(FsNode root, char sep)
        {
            var sb = new StringBuilder();
            sb.Append(Join(sep, "Path", "Size (bytes)", "Allocated (bytes)", "Files", "Folders", "Percent of parent", "Last modified"));
            sb.AppendLine();
            foreach (var n in root.EnumerateAll())
            {
                string modified = n.LastWriteUtc == DateTime.MinValue
                    ? ""
                    : n.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                sb.Append(Join(sep,
                    Quote(n.FullName, sep),
                    n.Size.ToString(),
                    n.Allocated.ToString(),
                    n.IsDirectory ? n.Files.ToString() : "",
                    n.IsDirectory ? n.Folders.ToString() : "",
                    n.PercentOfParent().ToString("0.##"),
                    modified));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        string Join(char sep, params string[] parts) => string.Join(sep.ToString(), parts);

        string Quote(string s, char sep)
        {
            if (s.IndexOf(sep) >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        void ShowLargestFiles()
        {
            if (_tree.RootFs == null) return;
            if (_largestForm != null && !_largestForm.IsDisposed)
            {
                _largestForm.Activate();
                return;
            }
            _largestForm = new LargestFilesForm(_tree.RootFs);
            _largestForm.FilesChanged += (s, e) => UpdateSummary();
            _largestForm.FormClosed += (s, e) => _largestForm = null;
            _largestForm.Show(this);
        }

        void ShowFileTypes()
        {
            if (_tree.RootFs == null) return;
            if (_typesForm != null && !_typesForm.IsDisposed)
            {
                _typesForm.Activate();
                return;
            }
            _typesForm = new FileTypesForm(_tree.RootFs);
            _typesForm.FormClosed += (s, e) => _typesForm = null;
            _typesForm.Show(this);
        }

        void ShowOptions()
        {
            using var dlg = new OptionsForm(_settings);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _settings.Save();

            _tree.Unit = Util.ParseUnit(_settings.Unit);
            _tree.ShowBars = _settings.ShowBars;
            _tree.BarColor = Util.ParseColor(_settings.BarColor, _tree.BarColor);
            _tree.RefreshAll();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (WindowState == FormWindowState.Normal)
            {
                var b = RestoreBounds;
                _settings.WindowRect = new[] { b.X, b.Y, b.Width, b.Height };
            }
            _settings.WindowMaximized = WindowState == FormWindowState.Maximized;

            var widths = new int[_tree.Columns.Count];
            var visible = new bool[_tree.Columns.Count];
            for (int i = 0; i < _tree.Columns.Count; i++)
            {
                widths[i] = _tree.Columns[i].Width;
                visible[i] = _tree.Columns[i].Visible;
            }
            _settings.ColumnWidths = widths;
            _settings.ColumnVisible = visible;
            _settings.SortColumn = _tree.SortColumn;
            _settings.SortAscending = _tree.SortAscending;
            _settings.Unit = _tree.Unit.ToString();
            _settings.ShowBars = _tree.ShowBars;
            if (!string.IsNullOrEmpty(_lastPath)) _settings.LastPath = _lastPath;
            _settings.Save();
        }
    }
}
