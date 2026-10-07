using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinHealthAudit
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var options = new AuditOptions();
            string error;

            if (!TryParse(args, options, out error))
            {
                Console.Error.WriteLine(error);
                PrintUsage();
                return 2;
            }

            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            // In JSON mode the document must be the only thing on stdout, so the
            // progress lines go to stderr where the window version can still read them.
            var context = new AuditContext(options, options.Json ? Console.Error : Console.Out);
            context.Trace("Windows Health Audit - " + context.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            context.Trace(string.Format(CultureInfo.InvariantCulture,
                "Looking back {0} day(s), elevated: {1}",
                options.WindowDays, context.IsElevated ? "yes" : "no"));
            context.Trace(string.Empty);

            var started = DateTime.Now;
            var results = new AuditRunner(HealthChecks.All()).Run(context);

            var reportPath = WriteReport(results, context);

            if (options.Json)
            {
                var seconds = (DateTime.Now - started).TotalSeconds;
                Console.Out.Write(JsonReport.Build(results, context, seconds, reportPath));
            }
            else
            {
                ReportWriter.PrintSummary(results, Console.Out, reportPath, context);
            }

            return Math.Min(99, ReportWriter.Count(results, Severity.Critical));
        }

        private static string WriteReport(IList<CheckResult> results, AuditContext context)
        {
            try
            {
                return ReportWriter.Write(results, context);
            }
            catch (Exception ex)
            {
                context.Trace("Could not write the report file: " + ex.Message);
                return "(report file not written)";
            }
        }

        private static bool TryParse(string[] args, AuditOptions options, out string error)
        {
            error = null;

            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];

                switch (argument)
                {
                    case "--days":
                    case "-d":
                        if (index + 1 >= args.Length)
                        {
                            error = argument + " needs a number";
                            return false;
                        }

                        int days;
                        if (!int.TryParse(args[++index], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out days) || days < 1 || days > 365)
                        {
                            error = "--days must be between 1 and 365";
                            return false;
                        }

                        options.WindowDays = days;
                        break;

                    case "--out":
                    case "-o":
                        if (index + 1 >= args.Length)
                        {
                            error = argument + " needs a directory";
                            return false;
                        }

                        options.OutputDirectory = args[++index];
                        break;

                    case "--quiet":
                    case "-q":
                        options.Quiet = true;
                        break;

                    case "--json":
                        options.Json = true;
                        break;

                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;

                    default:
                        error = "Unknown argument: " + argument;
                        return false;
                }
            }

            return true;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("WinHealthAudit - read-only health report for Windows");
            Console.WriteLine();
            Console.WriteLine("Usage: WinHealthAudit.Core.exe [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -d, --days N   look back N days in the event logs (default 14)");
            Console.WriteLine("  -o, --out DIR  where to write the markdown report (default .\\reports)");
            Console.WriteLine("  -q, --quiet    only print the summary");
            Console.WriteLine("      --json     print the result as one JSON document on stdout");
            Console.WriteLine("  -h, --help     show this help");
            Console.WriteLine();
            Console.WriteLine("Exit code: number of critical findings (0 = nothing critical).");
        }
    }
}
