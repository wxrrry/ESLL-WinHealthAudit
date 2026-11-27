using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WinHealthAudit.Window
{
    internal static class Launcher
    {
        private static int _port = 8777;
        private static string _webviewDir;
        private static Process _serverProc;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr handle, string text, string caption, uint type);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Run();
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox(IntPtr.Zero, ex.Message, "WinHealthAudit", 0x10);
                return 1;
            }
        }

        private static void Run()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var exePath = Assembly.GetEntryAssembly().Location;

            var appRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinHealthAudit");
            var dataDir = Path.Combine(appRoot, "gui");
            _webviewDir = Path.Combine(appRoot, "webview2");
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(_webviewDir);

            ExtractGuiBundle(dataDir);
            Extract(_webviewDir, "Microsoft.Web.WebView2.Core.dll", false);
            Extract(_webviewDir, "Microsoft.Web.WebView2.WinForms.dll", false);
            Extract(_webviewDir, "WebView2Loader.dll", false);
            SetDllDirectory(_webviewDir);

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var shortName = new AssemblyName(args.Name).Name + ".dll";
                var candidate = Path.Combine(_webviewDir, shortName);
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            };

            var script = Path.Combine(dataDir, "app.ps1");
            if (!File.Exists(script))
            {
                script = Extract(dataDir, "app.ps1", true);
                Extract(dataDir, "index.html", true);
                Extract(dataDir, "logo.png", true);
                Extract(dataDir, "logo.ico", false);
            }

            var engine = ExtractEngine(appRoot) ?? FindEngine(dir);

            if (!PageUp(_port) || IsElevated())
            {
                if (!IsPortFree(_port) && !IsElevated())
                {
                    _port = FindFreePort();
                }
                StartServer(script, engine, exePath, _port);
                WaitForServer(_port);
            }

            OpenStandaloneWindow(appRoot);
        }

        private static void OpenStandaloneWindow(string appRoot)
        {
            var url = "http://127.0.0.1:" + _port + "/";
            try
            {
                var form = new Form
                {
                    Text = "WinHealthAudit \\\\ ESLL",
                    Width = 1380,
                    Height = 860,
                    MinimumSize = new Size(1024, 660),
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor = Color.Black,
                    ForeColor = Color.White
                };

                try
                {
                    using (var s = Assembly.GetEntryAssembly().GetManifestResourceStream("logo.ico"))
                    {
                        if (s != null) form.Icon = new Icon(s);
                    }
                }
                catch { }

                form.HandleCreated += (s, e) =>
                {
                    try
                    {
                        int dark = 1;
                        DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
                    }
                    catch { }
                };

                var webView = new WebView2
                {
                    Dock = DockStyle.Fill,
                    DefaultBackgroundColor = Color.Black
                };
                form.Controls.Add(webView);

                form.Load += async (s, e) =>
                {
                    try
                    {
                        var userData = Path.Combine(appRoot, "ebwebview");
                        var env = await CoreWebView2Environment.CreateAsync(null, userData);
                        await webView.EnsureCoreWebView2Async(env);
                        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                        webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                        webView.CoreWebView2.Navigate(url);
                    }
                    catch
                    {
                        form.Close();
                        OpenBrowserFallback(url);
                    }
                };

                form.FormClosed += (s, e) =>
                {
                    try
                    {
                        if (_serverProc != null && !_serverProc.HasExited)
                            _serverProc.Kill();
                    }
                    catch { }
                };

                Application.Run(form);
            }
            catch
            {
                OpenBrowserFallback(url);
            }
        }

        private static void OpenBrowserFallback(string url)
        {
            var edges = new[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
            };
            foreach (var edge in edges)
            {
                if (File.Exists(edge))
                {
                    Process.Start(edge, "--app=\"" + url + "\"");
                    return;
                }
            }
            Process.Start(url);
        }

        private static bool IsElevated()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        private static string ExtractEngine(string installDir)
        {
            var engine = Extract(installDir, "WinHealthAudit.Core.exe", false);
            if (engine != null) return engine;
            var local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinHealthAudit.Core.exe");
            if (File.Exists(local)) return local;
            var inBin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "WinHealthAudit.Core.exe");
            if (File.Exists(inBin)) return inBin;
            return null;
        }

        private static string FindEngine(string dir)
        {
            var nextToUs = Path.Combine(dir, "WinHealthAudit.Core.exe");
            if (File.Exists(nextToUs)) return nextToUs;
            var inBin = Path.Combine(dir, "bin", "WinHealthAudit.Core.exe");
            if (File.Exists(inBin)) return inBin;
            throw new FileNotFoundException("WinHealthAudit.Core.exe was not found.");
        }

        private static void ExtractGuiBundle(string dataDir)
        {
            using (var stream = Assembly.GetEntryAssembly().GetManifestResourceStream("gui.zip"))
            {
                if (stream == null) return;
                var zipPath = Path.Combine(Path.GetDirectoryName(dataDir), "gui.zip");
                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                var marker = Path.Combine(dataDir, "app.ps1");
                if (File.Exists(zipPath) && File.Exists(marker) && Same(File.ReadAllBytes(zipPath), bytes)) return;
                File.WriteAllBytes(zipPath, bytes);
                using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        var dest = Path.GetFullPath(Path.Combine(dataDir, entry.FullName));
                        if (!dest.StartsWith(Path.GetFullPath(dataDir), StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        entry.ExtractToFile(dest, true);
                    }
                }
            }
        }

        private static string Extract(string dir, string name, bool required)
        {
            var target = Path.Combine(dir, name);
            using (var stream = Assembly.GetEntryAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    if (required) throw new InvalidOperationException("missing embedded file: " + name);
                    return null;
                }
                var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                if (!File.Exists(target) || !Same(File.ReadAllBytes(target), bytes))
                {
                    File.WriteAllBytes(target, bytes);
                }
            }
            return target;
        }

        private static bool Same(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index]) return false;
            }
            return true;
        }

        private static bool IsPortFree(int port)
        {
            try
            {
                var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        private static int FindFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        private static void StartServer(string script, string engine, string exePath, int port)
        {
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script +
                            "\" -NoBrowser -Port " + port +
                            " -Engine \"" + engine + "\"" +
                            " -Launcher \"" + exePath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _serverProc = Process.Start(info);
        }

        private static bool PageUp(int port)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/");
                request.Timeout = 900;
                request.Proxy = null;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var buffer = new char[400];
                    var read = reader.Read(buffer, 0, buffer.Length);
                    var head = new string(buffer, 0, read);
                    return head.IndexOf("WinHealthAudit", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        private static void WaitForServer(int port)
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                if (PageUp(port)) return;
                Thread.Sleep(250);
            }
            throw new InvalidOperationException("The local server did not start on port " + port);
        }
    }
}
