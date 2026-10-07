using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinHealthAudit
{
    /// <summary>Builds the JSON document that the window version reads from stdout.</summary>
    public static class JsonReport
    {
        public static string Build(IList<CheckResult> results, AuditContext context, double seconds, string reportPath)
        {
            var builder = new StringBuilder(8192);

            builder.Append("{\"machine\":").Append(Quote(Environment.MachineName));
            builder.Append(",\"user\":").Append(Quote(Environment.UserName));
            builder.Append(",\"generated\":").Append(Quote(
                context.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            builder.Append(",\"days\":").Append(context.Options.WindowDays.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"elevated\":").Append(context.IsElevated ? "true" : "false");
            builder.Append(",\"seconds\":").Append(seconds.ToString("0.0", CultureInfo.InvariantCulture));
            builder.Append(",\"report\":").Append(Quote(reportPath));

            builder.Append(",\"counts\":{\"critical\":")
                .Append(ReportWriter.Count(results, Severity.Critical))
                .Append(",\"warning\":").Append(ReportWriter.Count(results, Severity.Warning))
                .Append(",\"info\":").Append(ReportWriter.Count(results, Severity.Info))
                .Append("}");

            builder.Append(",\"checks\":[");
            for (var index = 0; index < results.Count; index++)
            {
                if (index > 0) builder.Append(',');
                var result = results[index];
                builder.Append("{\"name\":").Append(Quote(result.Name));
                builder.Append(",\"detail\":").Append(Quote(result.Detail));
                builder.Append(",\"findings\":").Append(result.Findings.Count.ToString(CultureInfo.InvariantCulture));
                builder.Append(",\"notes\":").Append(result.Notes.Count.ToString(CultureInfo.InvariantCulture));
                builder.Append('}');
            }
            builder.Append(']');

            builder.Append(",\"findings\":[");
            var first = true;
            foreach (var finding in Flatten(results))
            {
                if (!first) builder.Append(',');
                first = false;

                builder.Append("{\"severity\":").Append(Quote(SeverityWord(finding.Severity)));
                builder.Append(",\"source\":").Append(Quote(finding.Source));
                builder.Append(",\"message\":").Append(Quote(finding.Message));
                builder.Append(",\"evidence\":").Append(Quote(finding.Evidence));
                builder.Append(",\"rule\":").Append(Quote(finding.Rule));
                builder.Append(",\"detail\":").Append(Quote(finding.Detail));
                builder.Append(",\"advice\":").Append(Quote(finding.Advice));
                builder.Append('}');
            }
            builder.Append("]}");

            return builder.ToString();
        }

        /// <summary>Same order the report and the window use: worst first, then by source.</summary>
        private static List<Finding> Flatten(IList<CheckResult> results)
        {
            var findings = new List<Finding>();

            foreach (var result in results)
            {
                findings.AddRange(result.Findings);
            }

            findings.Sort(delegate(Finding left, Finding right)
            {
                if (left.Severity != right.Severity)
                {
                    return right.Severity.CompareTo(left.Severity);
                }

                return string.Compare(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);
            });

            return findings;
        }

        private static string SeverityWord(Severity level)
        {
            switch (level)
            {
                case Severity.Critical: return "critical";
                case Severity.Warning: return "warning";
                default: return "info";
            }
        }

        private static string Quote(string text)
        {
            if (text == null) text = string.Empty;

            var builder = new StringBuilder(text.Length + 2);
            builder.Append('"');

            foreach (var character in text)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
