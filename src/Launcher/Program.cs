using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WinHealthAudit.Window
{
    /// <summary>
    /// One-file launcher: unpacks the window (app.ps1 + index.html + engine) to
    /// %LOCALAPPDATA%\WinHealthAudit, starts the local server and opens an
    /// Edge --app window. The auxiliary engine travels inside this exe as
    /// WinHealthAudit.Core.exe.
    /// </summary>
    internal static class Launcher
    {
        private const int Port = 8777;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr handle, string text, string caption, uint type);

        private static int Main()
        {
            try
            {
                Run();
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox(IntPtr.Zero, ex.Message, "WinHealthAudit", 0x10 /* MB_ICONERROR */);
                return 1;
            }
        }

        private static void Run()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var exePath = Assembly.GetEntryAssembly().Location;

            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinHealthAudit", "gui");
            Directory.CreateDirectory(dataDir);

            var script = Extract(dataDir, "app.ps1", true);
            Extract(dataDir, "index.html", true);
            Extract(dataDir, "logo.png", true);

            var engine = ExtractEngine(Path.GetDirectoryName(dataDir)) ??
                         FindEngine(dir);

            // An elevated launch must always go through app.ps1: it takes the
            // port over from a non-elevated instance that may be running.
            if (!PageUp() || IsElevated())
            {
                StartServer(script, engine, exePath);
                WaitForServer();
            }

            OpenWindow();
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
            // One-file layout: the engine travels inside this exe - the single
            // source of truth, always in sync with the launcher build.
            var engine = Extract(installDir, "WinHealthAudit.Core.exe", false);
            if (engine != null) return engine;

            // Fallback for builds without the embedded engine.
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

            throw new FileNotFoundException(
                "WinHealthAudit.Core.exe was not found next to " +
                Path.GetFileName(Assembly.GetEntryAssembly().Location) +
                ". Put both files in the same folder.");
        }

        private static string Extract(string dir, string name, bool required)
        {
            var target = Path.Combine(dir, name);

            using (var stream = Assembly.GetEntryAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    if (required)
                    {
                        throw new InvalidOperationException("missing embedded file: " + name);
                    }
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

        private static void StartServer(string script, string engine, string exePath)
        {
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script +
                            "\" -NoBrowser -Port " + Port +
                            " -Engine \"" + engine + "\"" +
                            " -Launcher \"" + exePath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(info);
        }

        private static bool PageUp()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/");
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
            catch
            {
                return false;
            }
        }

        private static void WaitForServer()
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                if (PageUp()) return;
                Thread.Sleep(250);
            }

            throw new InvalidOperationException(
                "The local server did not start. Port " + Port +
                " may be busy - close the other window and try again.");
        }

        private static void OpenWindow()
        {
            var url = "http://127.0.0.1:" + Port + "/";

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
    }
}
