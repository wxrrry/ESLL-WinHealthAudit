using System;
using System.Windows.Forms;

namespace WinHealthAudit.Ui
{
    internal static class GuiProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }
}
