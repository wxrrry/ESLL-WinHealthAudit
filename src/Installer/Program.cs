using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
                InstallFiles(dir, true, true, null);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
            return 0;
        }

        internal static string EnsureProjectFolder(string chosenPath)
        {
            if (string.IsNullOrWhiteSpace(chosenPath)) return chosenPath;
            var trimmed = chosenPath.Trim().TrimEnd('\\', '/');
            var leaf = Path.GetFileName(trimmed);
            if (!string.Equals(leaf, "WinHealthAudit", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(trimmed, "WinHealthAudit");
            }
            return trimmed;
        }

        internal static void InstallFiles(string targetDir, bool desktopShortcut, bool startMenuShortcut, Action<int, string> onProgress)
        {
            if (onProgress != null) onProgress(15, "Creating directory...");
            Directory.CreateDirectory(targetDir);

            if (onProgress != null) onProgress(40, "Extracting WinHealthAudit.exe...");
            ExtractResource("WinHealthAudit.exe", Path.Combine(targetDir, "WinHealthAudit.exe"));

            if (onProgress != null) onProgress(75, "Extracting WinHealthAudit.Core.exe...");
            ExtractResource("WinHealthAudit.Core.exe", Path.Combine(targetDir, "WinHealthAudit.Core.exe"));

            var exePath = Path.Combine(targetDir, "WinHealthAudit.exe");
            if (onProgress != null) onProgress(90, "Creating shortcuts...");
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
            if (onProgress != null) onProgress(100, "Complete.");
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
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly TextBox _pathBox;
        private readonly CheckBox _chkDesktop;
        private readonly CheckBox _chkStart;
        private readonly CheckBox _chkLaunch;
        private readonly Button _btnInstall;
        private readonly Label _status;
        private readonly Panel _progTrack;
        private readonly Panel _progFill;

        public InstallerForm()
        {
            Text = "WinHealthAudit \\\\ ESLL — Setup v2.0.0";
            ClientSize = new Size(580, 410);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.Black;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            DoubleBuffered = true;

            try
            {
                using (var s = Assembly.GetEntryAssembly().GetManifestResourceStream("logo.ico"))
                {
                    if (s != null) Icon = new Icon(s);
                }
            }
            catch { }

            HandleCreated += (s, e) =>
            {
                try
                {
                    int dark = 1;
                    DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                }
                catch { }
            };

            var logoBox = new PictureBox
            {
                Location = new Point(32, 26),
                Size = new Size(38, 38),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            try
            {
                using (var s = Assembly.GetEntryAssembly().GetManifestResourceStream("logo.png"))
                {
                    if (s != null) logoBox.Image = Image.FromStream(s);
                }
            }
            catch { }

            var title = new Label
            {
                Text = "WINHEALTHAUDIT  \\\\  ESLL",
                Font = new Font("Segoe UI", 12.5f, FontStyle.Regular),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Location = new Point(82, 24),
                AutoSize = true
            };

            var sub = new Label
            {
                Text = "v2.0.0  ·  Standalone Windows Health & Control Suite",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(145, 145, 145),
                BackColor = Color.Transparent,
                Location = new Point(82, 48),
                AutoSize = true
            };

            var card = new Panel
            {
                Location = new Point(32, 86),
                Size = new Size(516, 232),
                BackColor = Color.FromArgb(14, 14, 14)
            };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(42, 42, 42), 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var lblPkg = new Label
            {
                Text = "COMPONENTS:  WinHealthAudit.exe  +  WinHealthAudit.Core.exe",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 120, 120),
                Location = new Point(20, 16),
                AutoSize = true
            };

            var lblDir = new Label
            {
                Text = "Installation folder / Папка установки (создаётся автоматически):",
                ForeColor = Color.FromArgb(205, 205, 205),
                Location = new Point(20, 46),
                AutoSize = true
            };

            _pathBox = new TextBox
            {
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinHealthAudit"),
                Location = new Point(20, 72),
                Size = new Size(376, 26),
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f)
            };

            var btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(406, 71),
                Size = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(26, 26, 26),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(68, 68, 68);
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Select base folder (WinHealthAudit folder will be created inside)";
                    dlg.SelectedPath = _pathBox.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _pathBox.Text = Program.EnsureProjectFolder(dlg.SelectedPath);
                    }
                }
            };

            _chkDesktop = new CheckBox
            {
                Text = "Create Desktop shortcut / Ярлык на рабочем столе",
                Checked = true,
                Location = new Point(20, 118),
                AutoSize = true,
                ForeColor = Color.FromArgb(215, 215, 215)
            };

            _chkStart = new CheckBox
            {
                Text = "Create Start Menu shortcut / Ярлык в меню Пуск",
                Checked = true,
                Location = new Point(20, 146),
                AutoSize = true,
                ForeColor = Color.FromArgb(215, 215, 215)
            };

            _chkLaunch = new CheckBox
            {
                Text = "Launch WinHealthAudit after install / Запустить после установки",
                Checked = true,
                Location = new Point(20, 174),
                AutoSize = true,
                ForeColor = Color.FromArgb(215, 215, 215)
            };

            card.Controls.AddRange(new Control[] { lblPkg, lblDir, _pathBox, btnBrowse, _chkDesktop, _chkStart, _chkLaunch });

            _progTrack = new Panel
            {
                Location = new Point(32, 336),
                Size = new Size(340, 4),
                BackColor = Color.FromArgb(32, 32, 32)
            };
            _progFill = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(0, 4),
                BackColor = Color.White
            };
            _progTrack.Controls.Add(_progFill);

            _status = new Label
            {
                Text = "Ready to install / Готово к установке",
                Location = new Point(32, 348),
                Size = new Size(340, 36),
                ForeColor = Color.FromArgb(145, 145, 145),
                BackColor = Color.Transparent
            };

            _btnInstall = new Button
            {
                Text = "Install / Установить",
                Location = new Point(388, 338),
                Size = new Size(160, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += OnInstallClick;

            Controls.AddRange(new Control[] { logoBox, title, sub, card, _progTrack, _status, _btnInstall });
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(26, 26, 26), Color.Black, 135f))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            var dir = Program.EnsureProjectFolder(_pathBox.Text);
            if (string.IsNullOrEmpty(dir)) return;
            _pathBox.Text = dir;

            _btnInstall.Enabled = false;
            try
            {
                Program.InstallFiles(dir, _chkDesktop.Checked, _chkStart.Checked, (pct, msg) =>
                {
                    _progFill.Width = (int)(_progTrack.Width * (pct / 100.0));
                    _status.Text = msg;
                    Application.DoEvents();
                });

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
