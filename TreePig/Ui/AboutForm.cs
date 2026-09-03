using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace TreePig.Ui
{
    class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "About TreePig";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(380, 170);
            Font = new Font("Segoe UI", 9f);

            var title = new Label
            {
                Text = "TreePig",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true
            };

            string version;
            try { version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
            catch { version = "0.1"; }

            var body = new Label
            {
                Text = "Version " + version + "\n\n" +
                       "A small disk space analyzer for Windows. It scans a " +
                       "folder and shows which subfolders take up the room.\n\n" +
                       "Free software under the MIT license.",
                Location = new Point(22, 55),
                Size = new Size(340, 90)
            };

            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(280, 135),
                Size = new Size(80, 26)
            };

            Controls.AddRange(new Control[] { title, body, ok });
            AcceptButton = ok;
            CancelButton = ok;
        }
    }
}
