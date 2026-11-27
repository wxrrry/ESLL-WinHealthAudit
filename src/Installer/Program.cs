using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinHealthAudit.Installer
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string silentDir = null;
            bool silent = false;
            bool desktop = true;
            bool startMenu = true;

            foreach (var a in args)
            {
                if (a.Equals("/S", StringComparison.OrdinalIgnoreCase) || a.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                    silent = true;
                else if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
                    silentDir = a.Substring(3).Trim('"');
                else if (a.Equals("/NODESKTOP", StringComparison.OrdinalIgnoreCase))
                    desktop = false;
                else if (a.Equals("/DESKTOP", StringComparison.OrdinalIgnoreCase))
                    desktop = true;
                else if (a.Equals("/NOSTARTMENU", StringComparison.OrdinalIgnoreCase))
                    startMenu = false;
                else if (a.Equals("/STARTMENU", StringComparison.OrdinalIgnoreCase))
                    startMenu = true;
            }

            if (silent)
            {
                var dir = EnsureProjectFolder(silentDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinHealthAudit"));
                InstallFiles(dir, desktop, startMenu, null);
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

            if (onProgress != null) onProgress(70, "Extracting WinHealthAudit.Core.exe...");
            ExtractResource("WinHealthAudit.Core.exe", Path.Combine(targetDir, "WinHealthAudit.Core.exe"));

            var exePath = Path.Combine(targetDir, "WinHealthAudit.exe");

            if (onProgress != null) onProgress(85, "Registering in Windows Search & Start Menu...");
            RegisterWindowsApp(targetDir, exePath);

            if (desktopShortcut)
            {
                var desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "WinHealthAudit.lnk");
                CreateShortcut(desk, exePath, targetDir);
            }
            if (startMenuShortcut)
            {
                var smFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "WinHealthAudit");
                Directory.CreateDirectory(smFolder);
                CreateShortcut(Path.Combine(smFolder, "WinHealthAudit.lnk"), exePath, targetDir);
            }

            if (onProgress != null) onProgress(100, "Installation complete.");
        }

        private static void RegisterWindowsApp(string targetDir, string exePath)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\WinHealthAudit.exe"))
                {
                    if (key != null)
                    {
                        key.SetValue("", exePath);
                        key.SetValue("Path", targetDir);
                    }
                }
                using (var ukey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinHealthAudit"))
                {
                    if (ukey != null)
                    {
                        ukey.SetValue("DisplayName", "WinHealthAudit \\\\ ESLL");
                        ukey.SetValue("DisplayVersion", "2.0.0");
                        ukey.SetValue("Publisher", "ESLL");
                        ukey.SetValue("InstallLocation", targetDir);
                        ukey.SetValue("DisplayIcon", exePath + ",0");
                        ukey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        ukey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
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

    internal sealed class EsllToggle : Control
    {
        public bool Checked { get; set; }

        public EsllToggle(string text, bool initial)
        {
            Text = text;
            Checked = initial;
            Size = new Size(460, 26);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            Invalidate();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var boxRect = new Rectangle(0, 4, 16, 16);
            using (var pen = new Pen(Checked ? Color.White : Color.FromArgb(90, 90, 90), 1.3f))
                e.Graphics.DrawRectangle(pen, boxRect);

            if (Checked)
            {
                using (var b = new SolidBrush(Color.White))
                    e.Graphics.FillRectangle(b, 3, 7, 10, 10);
            }

            using (var tb = new SolidBrush(Checked ? Color.FromArgb(240, 240, 240) : Color.FromArgb(150, 150, 150)))
                e.Graphics.DrawString(Text, Font, tb, 26, 3);
        }
    }

    internal sealed class InstallerForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private readonly TextBox _pathBox;
        private readonly EsllToggle _chkDesktop;
        private readonly EsllToggle _chkStart;
        private readonly EsllToggle _chkLaunch;
        private readonly Button _btnInstall;
        private readonly Label _status;
        private readonly Panel _progTrack;
        private readonly Panel _progFill;

        public InstallerForm()
        {
            Text = "WinHealthAudit \\\\ ESLL — Setup v2.0.0";
            ClientSize = new Size(600, 430);
            FormBorderStyle = FormBorderStyle.None;
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

            MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && e.Y < 80)
                {
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };

            var btnClose = new Label
            {
                Text = "✕",
                Location = new Point(558, 16),
                Size = new Size(28, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(140, 140, 140),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = Color.White;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = Color.FromArgb(140, 140, 140);
            btnClose.Click += (s, e) => Close();

            var btnMin = new Label
            {
                Text = "—",
                Location = new Point(526, 16),
                Size = new Size(28, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(140, 140, 140),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btnMin.MouseEnter += (s, e) => btnMin.ForeColor = Color.White;
            btnMin.MouseLeave += (s, e) => btnMin.ForeColor = Color.FromArgb(140, 140, 140);
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;

            var logoBox = new PictureBox
            {
                Location = new Point(34, 28),
                Size = new Size(42, 42),
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
                Font = new Font("Segoe UI", 13f, FontStyle.Regular),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Location = new Point(90, 26),
                AutoSize = true
            };

            var sub = new Label
            {
                Text = "VERSION 2.0.0  ·  WINDOWS HEALTH & CONTROL SUITE",
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(125, 125, 125),
                BackColor = Color.Transparent,
                Location = new Point(90, 52),
                AutoSize = true
            };

            var card = new Panel
            {
                Location = new Point(34, 92),
                Size = new Size(532, 246),
                BackColor = Color.FromArgb(12, 12, 12)
            };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(40, 40, 40), 1))
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var lblPkg = new Label
            {
                Text = "PACKAGE:  WinHealthAudit.exe (UI)  +  WinHealthAudit.Core.exe (Engine)",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(115, 115, 115),
                Location = new Point(22, 18),
                AutoSize = true
            };

            var lblDir = new Label
            {
                Text = "Installation directory / Папка установки (папка WinHealthAudit создаётся сама):",
                ForeColor = Color.FromArgb(205, 205, 205),
                Location = new Point(22, 48),
                AutoSize = true
            };

            _pathBox = new TextBox
            {
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinHealthAudit"),
                Location = new Point(22, 74),
                Size = new Size(388, 26),
                BackColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f)
            };

            var btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(420, 73),
                Size = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(26, 26, 26),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
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

            _chkDesktop = new EsllToggle("Add WinHealthAudit shortcut to Desktop / Ярлык на рабочий стол", true) { Location = new Point(22, 122) };
            _chkStart   = new EsllToggle("Add to Start Menu & Windows Search / Добавить в Пуск и поиск", true) { Location = new Point(22, 154) };
            _chkLaunch  = new EsllToggle("Launch WinHealthAudit after install / Запустить после установки", true) { Location = new Point(22, 186) };

            card.Controls.AddRange(new Control[] { lblPkg, lblDir, _pathBox, btnBrowse, _chkDesktop, _chkStart, _chkLaunch });

            _progTrack = new Panel
            {
                Location = new Point(34, 358),
                Size = new Size(348, 3),
                BackColor = Color.FromArgb(34, 34, 34)
            };
            _progFill = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(0, 3),
                BackColor = Color.White
            };
            _progTrack.Controls.Add(_progFill);

            _status = new Label
            {
                Text = "Ready to install / Готово к установке",
                Location = new Point(34, 370),
                Size = new Size(348, 36),
                ForeColor = Color.FromArgb(145, 145, 145),
                BackColor = Color.Transparent
            };

            _btnInstall = new Button
            {
                Text = "Install / Установить",
                Location = new Point(402, 356),
                Size = new Size(164, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += OnInstallClick;

            Controls.AddRange(new Control[] { btnClose, btnMin, logoBox, title, sub, card, _progTrack, _status, _btnInstall });
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(24, 24, 24), Color.Black, 135f))
                e.Graphics.FillRectangle(brush, ClientRectangle);
            using (var pen = new Pen(Color.FromArgb(46, 46, 46), 1))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
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
