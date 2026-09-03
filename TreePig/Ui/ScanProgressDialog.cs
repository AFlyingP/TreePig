using System;
using System.Drawing;
using System.Windows.Forms;
using TreePig.Core;

namespace TreePig.Ui
{
    // small modeless window shown while a scan runs, cancel stops the scan
    class ScanProgressDialog : Form
    {
        readonly Label _pathLabel;
        readonly Label _statLabel;
        readonly Button _cancel;
        readonly Action _onCancel;
        bool _cancelAsked;

        public ScanProgressDialog(Action onCancel)
        {
            _onCancel = onCancel;
            Text = "Scanning...";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 116);
            Font = new Font("Segoe UI", 9f);

            _pathLabel = new Label
            {
                Location = new Point(12, 12),
                Size = new Size(496, 18),
                Text = ""
            };

            _statLabel = new Label
            {
                Location = new Point(12, 36),
                Size = new Size(496, 40),
                Text = ""
            };

            _cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(424, 82),
                Size = new Size(84, 26)
            };
            _cancel.Click += (s, e) =>
            {
                _cancelAsked = true;
                _cancel.Enabled = false;
                _cancel.Text = "Cancelling...";
                _onCancel?.Invoke();
            };

            Controls.AddRange(new Control[] { _pathLabel, _statLabel, _cancel });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // clicking the X counts as cancel too
            if (!_cancelAsked && e.CloseReason == CloseReason.UserClosing)
                _onCancel?.Invoke();
        }

        public void UpdateProgress(ScanProgress p)
        {
            _pathLabel.Text = FitPath(p.CurrentPath, _pathLabel.Width);
            double rate = p.Elapsed.TotalSeconds > 0.2 ? p.Files / p.Elapsed.TotalSeconds : 0;
            _statLabel.Text = string.Format("{0} in {1} files, {2} folders\n{3} elapsed, {4:0} files/s",
                Util.FormatBytes(p.Bytes),
                p.Files.ToString("N0"),
                p.Dirs.ToString("N0"),
                Util.FormatElapsed(p.Elapsed),
                rate);
        }

        // paths get long, trim them from the left so the interesting end
        // stays visible
        string FitPath(string path, int availWidth)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string s = path;
            while (s.Length > 12 && TextRenderer.MeasureText(s, Font).Width > availWidth)
                s = "..." + s.Substring(8);
            return s;
        }
    }
}
