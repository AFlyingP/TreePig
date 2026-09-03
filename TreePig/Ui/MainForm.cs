using System.Windows.Forms;

namespace TreePig.Ui
{
    class MainForm : Form
    {
        public MainForm(string[] args)
        {
            Text = "TreePig";
            Font = new Font("Segoe UI", 9f);
            Size = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
        }
    }
}
