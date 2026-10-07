using System;
using System.IO;
using System.Security.Principal;

namespace WinHealthAudit
{
    public sealed class AuditContext
    {
        public AuditContext(AuditOptions options, TextWriter console)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (console == null) throw new ArgumentNullException("console");

            Options = options;
            Console = console;
            StartedAt = DateTime.Now;
            IsElevated = IsAdministrator();
        }

        /// <summary>True when the current process runs with administrator rights.</summary>
        public static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public AuditOptions Options { get; private set; }

        public TextWriter Console { get; private set; }

        public DateTime StartedAt { get; private set; }

        /// <summary>True when the process runs with administrator rights.</summary>
        public bool IsElevated { get; private set; }

        public TimeSpan Window
        {
            get { return TimeSpan.FromDays(Options.WindowDays); }
        }

        public void Trace(string text)
        {
            if (Options.Quiet) return;

            Console.WriteLine(text);
            // The window reads the progress straight from the redirected stderr,
            // so every line has to hit the file right away.
            Console.Flush();
        }
    }
}
