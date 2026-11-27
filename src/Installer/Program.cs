using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinHealthAudit.Installer
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string silentDir = null;
            bool silent = false;
            foreach (var a in args)
            {
                if (a.Equals("/S", StringComparison.OrdinalIgnoreCase) || a.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                    silent = true;
                else if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
                    silentDir = a.Substring(3).Trim('"');
            }

            if (silent)
            {
                var dir = silentDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinHealthAudit");
                InstallFiles(dir, true, true);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
            return 0;
        }

        internal static void InstallFiles(string targetDir, bool desktopShortcut, bool startMenuShortcut)
        {
            Directory.CreateDirectory(targetDir);
            ExtractResource("WinHealthAudit.exe", Path.Combine(targetDir, "WinHealthAudit.exe"));
            ExtractResource("WinHealthAudit.Core.exe", Path.Combine(targetDir, "WinHealthAudit.Core.exe"));

            var exePath = Path.Combine(targetDir, "WinHealthAudit.exe");
            if (desktopShortcut)
            {
                var desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WinHealthAudit.lnk");
                CreateShortcut(desk, exePath, targetDir);
            }
            if (startMenuShortcut)
            {
                var sm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "WinHealthAudit.lnk");
                CreateShortcut(sm, exePath, targetDir);
            }
        }

        private static void ExtractResource(string name, string dest)
        {
            using (var s = Assembly.GetEntryAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new InvalidOperationException("Missing embedded resource: " + name);
                using (var fs = File.Create(dest))
                {
                    s.CopyTo(fs);
                }
            }
        }

        private static void CreateShortcut(string lnkPath, string targetExe, string workDir)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                dynamic shell = Activator.CreateInstance(t);
                var sc = shell.CreateShortcut(lnkPath);
                sc.TargetPath = targetExe;
                sc.WorkingDirectory = workDir;
                sc.IconLocation = targetExe + ",0";
                sc.Description = "WinHealthAudit \\ ESLL";
                sc.Save();
                Marshal.FinalReleaseComObject(sc);
                Marshal.FinalReleaseComObject(shell);
            }
            catch { }
        }
    }

    internal sealed class InstallerForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly CheckBox _chkDesktop;
        private readonly CheckBox _chkStart;
        private readonly CheckBox _chkLaunch;
        private readonly Button _btnInstall;
        private readonly Label _status;

        public InstallerForm()
        {
            Text = "WinHealthAudit \\\\ ESLL — Setup v2.0.0";
            ClientSize = new Size(540, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.Black;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            try
            {
                using (var s = Assembly.GetEntryAssembly().GetManifestResourceStream("logo.ico"))
                {
                    if (s != null) Icon = new Icon(s);
                }
            }
            catch { }

            var title = new Label
            {
                Text = "WINHEALTHAUDIT \\\\ ESLL",
                Font = new Font("Segoe UI", 14f, FontStyle.Regular),
                ForeColor = Color.White,
                Location = new Point(28, 24),
                AutoSize = true
            };

            var sub = new Label
            {
                Text = "v2.0.0 — Installs WinHealthAudit.exe & WinHealthAudit.Core.exe",
                ForeColor = Color.FromArgb(150, 150, 150),
                Location = new Point(28, 56),
                AutoSize = true
            };

            var lblDir = new Label
            {
                Text = "Installation folder / Папка установки:",
                ForeColor = Color.FromArgb(190, 190, 190),
                Location = new Point(28, 100),
                AutoSize = true
            };

            _pathBox = new TextBox
            {
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinHealthAudit"),
                Location = new Point(28, 124),
                Size = new Size(388, 26),
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(426, 123),
                Size = new Size(86, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.White
            };
            btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = _pathBox.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dlg.SelectedPath;
                }
            };

            _chkDesktop = new CheckBox
            {
                Text = "Create Desktop shortcut / Ярлык на рабочем столе",
                Checked = true,
                Location = new Point(28, 170),
                AutoSize = true,
                ForeColor = Color.FromArgb(210, 210, 210)
            };

            _chkStart = new CheckBox
            {
                Text = "Create Start Menu shortcut / Ярлык в меню Пуск",
                Checked = true,
                Location = new Point(28, 198),
                AutoSize = true,
                ForeColor = Color.FromArgb(210, 210, 210)
            };

            _chkLaunch = new CheckBox
            {
                Text = "Launch WinHealthAudit after install / Запустить после установки",
                Checked = true,
                Location = new Point(28, 226),
                AutoSize = true,
                ForeColor = Color.FromArgb(210, 210, 210)
            };

            _status = new Label
            {
                Text = "",
                Location = new Point(28, 268),
                Size = new Size(340, 40),
                ForeColor = Color.FromArgb(170, 170, 170)
            };

            _btnInstall = new Button
            {
                Text = "Install / Установить",
                Location = new Point(372, 278),
                Size = new Size(140, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += OnInstallClick;

            Controls.AddRange(new Control[] { title, sub, lblDir, _pathBox, btnBrowse, _chkDesktop, _chkStart, _chkLaunch, _status, _btnInstall });
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            var dir = _pathBox.Text.Trim();
            if (string.IsNullOrEmpty(dir)) return;

            _btnInstall.Enabled = false;
            _status.Text = "Installing WinHealthAudit.exe & WinHealthAudit.Core.exe...";
            Application.DoEvents();

            try
            {
                Program.InstallFiles(dir, _chkDesktop.Checked, _chkStart.Checked);
                _status.Text = "Installed to: " + dir;
                if (_chkLaunch.Checked)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(dir, "WinHealthAudit.exe"),
                        WorkingDirectory = dir
                    });
                }
                Close();
            }
            catch (Exception ex)
            {
                _btnInstall.Enabled = true;
                _status.Text = "Error: " + ex.Message;
            }
        }
    }
}
