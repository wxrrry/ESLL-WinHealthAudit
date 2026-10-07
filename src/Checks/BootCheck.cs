using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml;
using Microsoft.Win32;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class BootCheck : IHealthCheck
    {
        public string Name { get { return "Boot performance"; } }

        private const string BootLog = "Microsoft-Windows-Kernel-Boot/Operational";
        private const string DiagLog = "Microsoft-Windows-Diagnostics-Performance/Operational";
        private const int MaxRecords = 5000;
        private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(10);

        private sealed class RawEvent
        {
            public DateTime Time;
            public int Id;
            public Dictionary<string, string> Data;
        }

        private sealed class BootRecord
        {
            public DateTime Time;
            public int Id;
            public double Seconds;
            public double MajorThreshold;
            public bool Degraded;
        }

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name,
                "Boots in the window, boot timing from Diagnostics-Performance and Fast Startup state");

            DescribeBootCount(result, context);

            if (context.IsElevated)
            {
                DescribeBootTiming(result, context);
            }
            else
            {
                result.Note("Not elevated: boot duration timings (Diagnostics-Performance log) are skipped.");
            }

            DescribeFastStartup(result);

            return result;
        }

        private static void DescribeBootCount(CheckResult result, AuditContext context)
        {
            List<RawEvent> events = ReadEvents(BootLog, context);

            if (events == null)
            {
                result.Note("Kernel-Boot log is not readable; boot count skipped.");
                return;
            }

            if (events.Count == 0)
            {
                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "No Kernel-Boot events in the last {0} day(s).", context.Options.WindowDays));
                return;
            }

            var times = new List<DateTime>();
            foreach (var item in events) times.Add(item.Time);
            times.Sort();

            var sessions = 1;
            for (var index = 1; index < times.Count; index++)
            {
                if (times[index] - times[index - 1] > SessionGap) sessions++;
            }

            var days = context.Options.WindowDays > 0 ? context.Options.WindowDays : 1;
            result.Note(string.Format(CultureInfo.InvariantCulture,
                "Boots detected in the last {0} day(s): {1} (about {2:0.0} per day).",
                days, sessions, sessions / (double)days));
        }

        private static void DescribeBootTiming(CheckResult result, AuditContext context)
        {
            List<RawEvent> events = ReadEvents(DiagLog, context);

            if (events == null)
            {
                result.Note("Diagnostics-Performance log is not readable; boot timings skipped.");
                return;
            }

            var records = new List<BootRecord>();
            var byId = new Dictionary<int, int>();

            foreach (var item in events)
            {
                var raw = Text(item.Data, "BootTime");
                if (raw.Length == 0)
                {
                    Increment(byId, item.Id);
                    continue;
                }

                var seconds = ToSeconds(raw);
                if (seconds < 0)
                {
                    Increment(byId, item.Id);
                    continue;
                }

                var record = new BootRecord();
                record.Time = item.Time;
                record.Id = item.Id;
                record.Seconds = seconds;

                var flag = Text(item.Data, "IsDegradation");
                if (flag.Length == 0) flag = Text(item.Data, "BootIsDegradation");
                record.Degraded = IsSet(flag);

                var threshold = ToSeconds(Text(item.Data, "BootMajorThreshold_Sec"));
                if (threshold >= 0)
                {
                    record.MajorThreshold = threshold;
                    if (seconds > threshold) record.Degraded = true;
                }

                records.Add(record);
            }

            if (records.Count == 0)
            {
                result.Note("No boot performance records in the window.");
                if (byId.Count > 0)
                {
                    result.Note("Other Diagnostics-Performance event ids: " + FormatEventIds(byId) + ".");
                }
                return;
            }

            var latest = records[0];
            var worst = records[0];
            var degraded = 0;
            double total = 0;

            foreach (var record in records)
            {
                total += record.Seconds;
                if (record.Degraded) degraded++;
                if (record.Time > latest.Time) latest = record;
                if (record.Seconds > worst.Seconds) worst = record;
            }

            result.Note(string.Format(CultureInfo.InvariantCulture,
                "Boot records in the window: {0} (latest {1:0.0} s, average {2:0.0} s, worst {3:0.0} s).",
                records.Count, latest.Seconds, total / records.Count, worst.Seconds));

            result.Signal("bootSec", Math.Round(latest.Seconds, 1));

            if (byId.Count > 0)
            {
                result.Note("Other Diagnostics-Performance event ids: " + FormatEventIds(byId) + ".");
            }

            if (degraded > 0)
            {
                result.Warn(string.Format(CultureInfo.InvariantCulture,
                    "Windows flagged {0} slow boot(s) in the window", degraded),
                    string.Format(CultureInfo.InvariantCulture,
                        "worst boot {0:0.0} s at {1:yyyy-MM-dd HH:mm} (event id {2})",
                        worst.Seconds, worst.Time, worst.Id),
                    "boot.degraded", "Diagnostics-Performance boot records with IsDegradation",
                    "Settings > Apps > Startup - disable apps you do not need at logon, then reboot and re-check");
            }
            else if (worst.Seconds >= 60)
            {
                result.Warn(string.Format(CultureInfo.InvariantCulture,
                    "A boot took {0:0.0} s in the window", worst.Seconds),
                    string.Format(CultureInfo.InvariantCulture,
                        "worst boot at {0:yyyy-MM-dd HH:mm} (event id {1})",
                        worst.Time, worst.Id),
                    "boot.degraded", "Diagnostics-Performance BootTime field",
                    "Settings > Apps > Startup - disable apps you do not need at logon, then reboot and re-check");
            }
        }

        private static void DescribeFastStartup(CheckResult result)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power"))
                {
                    var value = key == null ? null : key.GetValue("HiberbootEnabled");
                    if (value == null)
                    {
                        result.Note("Fast Startup: not configured (HiberbootEnabled absent).");
                        return;
                    }

                    var on = Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
                    result.Note(on
                        ? "Fast Startup: on (Windows default - cold boots resume from the hibernation image)."
                        : "Fast Startup: off (every cold boot starts from scratch).");
                }
            }
            catch (Exception ex)
            {
                result.Note("Fast Startup state unavailable: " + ex.Message);
            }
        }

        private static List<RawEvent> ReadEvents(string log, AuditContext context)
        {
            var filter = string.Format(CultureInfo.InvariantCulture,
                "*[System[TimeCreated[timediff(@SystemTime) <= {0}]]]",
                (long)context.Window.TotalMilliseconds);

            var events = new List<RawEvent>();

            try
            {
                var query = new EventLogQuery(log, PathType.LogName, filter);

                using (var reader = new EventLogReader(query))
                {
                    EventRecord record;
                    while ((record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            var item = new RawEvent();
                            item.Time = record.TimeCreated ?? DateTime.MinValue;
                            item.Id = record.Id;
                            item.Data = ParseData(record);
                            events.Add(item);
                        }

                        if (events.Count >= MaxRecords) break;
                    }
                }
            }
            catch (EventLogException)
            {
                return null;
            }

            return events;
        }

        private static Dictionary<string, string> ParseData(EventRecord record)
        {
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(record.ToXml());

                var names = new XmlNamespaceManager(doc.NameTable);
                names.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");

                foreach (XmlNode node in doc.SelectNodes("/e:Event/e:EventData/e:Data", names))
                {
                    var element = node as XmlElement;
                    if (element == null) continue;

                    var key = element.GetAttribute("Name");
                    if (key.Length > 0) data[key] = element.InnerText;
                }
            }
            catch (XmlException)
            {
                // Event payload is not XML; the raw fields stay empty.
            }

            return data;
        }

        private static double ToSeconds(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return -1;

            double value;
            if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return -1;
            }

            // Boot durations arrive either in milliseconds (the usual case) or in seconds.
            // Anything at or above 1000 cannot be a boot length in seconds, so it is milliseconds.
            if (value >= 1000) value = value / 1000.0;
            return value;
        }

        private static bool IsSet(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;

            var text = raw.Trim();
            return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string Text(IDictionary<string, string> data, string property)
        {
            string value;
            if (data.TryGetValue(property, out value) && value != null) return value.Trim();
            return string.Empty;
        }

        private static void Increment(Dictionary<int, int> counters, int key)
        {
            int current;
            counters.TryGetValue(key, out current);
            counters[key] = current + 1;
        }

        private static string FormatEventIds(Dictionary<int, int> counters)
        {
            var parts = new List<string>();
            foreach (var pair in counters)
            {
                parts.Add(pair.Key + "x" + pair.Value);
            }

            return string.Join(", ", parts.ToArray());
        }
    }
}
