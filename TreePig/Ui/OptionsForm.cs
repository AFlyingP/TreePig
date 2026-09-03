using System;
using System.Drawing;
using System.Windows.Forms;
using TreePig.Core;

namespace TreePig.Ui
{
    class OptionsForm : Form
    {
        readonly AppSettings _settings;
        readonly ComboBox _unit;
        readonly CheckBox _bars, _ownerInfo, _scanLast;
        readonly Button _colorButton;
        Color _barColor;

        public OptionsForm(AppSettings settings)
        {
            _settings = settings;
            Text = "Options";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 220);
            Font = new Font("Segoe UI", 9f);

            var scanGroup = new GroupBox
            {
                Text = "Scanning",
                Location = new Point(12, 10),
                Size = new Size(406, 74)
            };
            _ownerInfo = new CheckBox
            {
                Text = "Collect owner information (slower scans)",
                Location = new Point(12, 22),
                AutoSize = true,
                Checked = _settings.CollectOwner
            };
            _scanLast = new CheckBox
            {
                Text = "Scan the last folder again at startup",
                Location = new Point(12, 44),
                AutoSize = true,
                Checked = _settings.ScanLastOnStart
            };
            scanGroup.Controls.AddRange(new Control[] { _ownerInfo, _scanLast });

            var viewGroup = new GroupBox
            {
                Text = "View",
                Location = new Point(12, 92),
                Size = new Size(406, 74)
            };
            var unitLabel = new Label { Text = "Size units:", Location = new Point(12, 28), AutoSize = true };
            _unit = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(90, 24),
                Width = 100
            };
            foreach (SizeUnit u in Enum.GetValues(typeof(SizeUnit)))
                _unit.Items.Add(u.ToString());
            _unit.SelectedItem = _settings.Unit;

            _bars = new CheckBox
            {
                Text = "Show size bars",
                Location = new Point(210, 26),
                AutoSize = true,
                Checked = _settings.ShowBars
            };

            _colorButton = new Button
            {
                Text = "Bar color...",
                Location = new Point(90, 40 - 8),
                Size = new Size(100, 26)
            };
            _colorButton.Click += (s, e) =>
            {
                using var dlg = new ColorDialog { Color = _barColor, FullOpen = true };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _barColor = dlg.Color;
                    _colorButton.BackColor = _barColor;
                }
            };
            viewGroup.Controls.AddRange(new Control[] { unitLabel, _unit, _bars, _colorButton });
            _barColor = Util.ParseColor(_settings.BarColor, Color.FromArgb(192, 80, 77));
            _colorButton.BackColor = _barColor;

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(246, 182), Size = new Size(80, 26) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(334, 182), Size = new Size(80, 26) };

            Controls.AddRange(new Control[] { scanGroup, viewGroup, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (DialogResult != DialogResult.OK) return;
            _settings.Unit = _unit.SelectedItem?.ToString() ?? "Auto";
            _settings.ShowBars = _bars.Checked;
            _settings.CollectOwner = _ownerInfo.Checked;
            _settings.ScanLastOnStart = _scanLast.Checked;
            _settings.BarColor = string.Format("{0},{1},{2}", _barColor.R, _barColor.G, _barColor.B);
        }
    }
}
