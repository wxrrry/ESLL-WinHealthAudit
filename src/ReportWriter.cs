using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WinHealthAudit
{
    public static class ReportWriter
    {
        public static int Count(IList<CheckResult> results, Severity severity)
        {
            return Flatten(results).Count(finding => finding.Severity == severity);
        }

        public static string Write(IList<CheckResult> results, AuditContext context)
        {
            if (!Directory.Exists(context.Options.OutputDirectory))
            {
                Directory.CreateDirectory(context.Options.OutputDirectory);
            }

            var fileName = string.Format(
                CultureInfo.InvariantCulture,
                "WinHealthAudit-{0:yyyyMMdd-HHmmss}.md",
                context.StartedAt);

            var path = Path.Combine(context.Options.OutputDirectory, fileName);
            File.WriteAllText(path, BuildMarkdown(results, context), new UTF8Encoding(true));
            return path;
        }

        public static void PrintSummary(IList<CheckResult> results, TextWriter output, string reportPath, AuditContext context)
        {
            var findings = Flatten(results);

            output.WriteLine();
            output.WriteLine("Summary: {0} critical, {1} warning, {2} info",
                Count(results, Severity.Critical),
                Count(results, Severity.Warning),
                Count(results, Severity.Info));

            output.WriteLine();

            foreach (var finding in findings.Where(f => f.Severity == Severity.Critical || f.Severity == Severity.Warning))
            {
                output.WriteLine("  [{0}] {1} - {2}",
                    finding.Severity == Severity.Critical ? "CRITICAL" : "WARNING",
                    finding.Source,
                    finding.Message);
            }

            if (!context.IsElevated)
            {
                output.WriteLine();
                output.WriteLine("  Note: run from an elevated prompt for SMART details and full log access.");
            }

            output.WriteLine();
            output.WriteLine("Report: {0}", reportPath);
        }

        private static string BuildMarkdown(IList<CheckResult> results, AuditContext context)
        {
            var findings = Flatten(results);
            var builder = new StringBuilder();

            builder.AppendLine("# Windows Health Audit");
            builder.AppendLine();
            builder.AppendLine("- Machine: `" + Environment.MachineName + "\\" + Environment.UserName + "`");
            builder.AppendLine("- Generated: " + context.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("- Event window: last " + context.Options.WindowDays + " days");
            builder.AppendLine("- Elevated: " + (context.IsElevated ? "yes" : "no"));
            builder.AppendLine("- Checks ran: " + results.Count);
            builder.AppendLine("- Health: " + JsonReport.Health(results) + " / 100");
            builder.AppendLine();

            builder.AppendLine("## Summary");
            builder.AppendLine();
            builder.AppendLine("| Severity | Count |");
            builder.AppendLine("| --- | ---: |");
            builder.AppendLine("| Critical | " + Count(results, Severity.Critical) + " |");
            builder.AppendLine("| Warning | " + Count(results, Severity.Warning) + " |");
            builder.AppendLine("| Info | " + Count(results, Severity.Info) + " |");
            builder.AppendLine();

            AppendFindings(builder, "Critical findings", findings.Where(f => f.Severity == Severity.Critical));
            AppendFindings(builder, "Warning findings", findings.Where(f => f.Severity == Severity.Warning));

            builder.AppendLine("## Check results");
            builder.AppendLine();

            foreach (var result in results)
            {
                builder.AppendLine("### " + result.Name);
                builder.AppendLine();

                if (result.Detail.Length > 0)
                {
                    builder.AppendLine("> What was checked: " + result.Detail);
                    builder.AppendLine();
                }

                if (result.Notes.Count > 0)
                {
                    foreach (var note in result.Notes)
                    {
                        builder.AppendLine("- " + note);
                    }

                    builder.AppendLine();
                }

                if (result.Findings.Count > 0)
                {
                    foreach (var finding in result.Findings)
                    {
                        var line = "- **" + finding.Severity + "** - " + finding.Message;
                        if (finding.Evidence.Length > 0)
                        {
                            line += "  \n  `" + finding.Evidence + "`";
                        }

                        builder.AppendLine(line);
                    }

                    builder.AppendLine();
                }
            }

            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine("*Generated by WinHealthAudit. The tool only reads system state; nothing is changed.*");

            return builder.ToString();
        }

        private static void AppendFindings(StringBuilder builder, string title, IEnumerable<Finding> findings)
        {
            var list = findings.ToList();
            if (list.Count == 0) return;

            builder.AppendLine("## " + title);
            builder.AppendLine();

            foreach (var finding in list)
            {
                builder.AppendLine("1. **" + finding.Source + "** - " + finding.Message);
                if (finding.Evidence.Length > 0)
                {
                    builder.AppendLine("   - Evidence: `" + finding.Evidence + "`");
                }

                if (finding.Detail.Length > 0)
                {
                    builder.AppendLine("   - Checked: " + finding.Detail);
                }

                if (finding.Advice.Length > 0)
                {
                    builder.AppendLine("   - Do: " + finding.Advice);
                }
            }

            builder.AppendLine();
        }

        private static List<Finding> Flatten(IList<CheckResult> results)
        {
            var findings = new List<Finding>();

            foreach (var result in results)
            {
                findings.AddRange(result.Findings);
            }

            return findings.OrderByDescending(f => (int)f.Severity).ToList();
        }
    }
}
