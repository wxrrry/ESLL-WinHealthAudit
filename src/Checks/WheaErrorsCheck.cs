using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class WheaErrorsCheck : IHealthCheck
    {
        public string Name { get { return "WHEA hardware errors"; } }

        private const string Provider = "Microsoft-Windows-WHEA-Logger";
        private const int MaxRecords = 100000;

        private static readonly string[] CorrectableBits =
        {
            "receiver error",
            "bad TLP",
            "bad DLLP",
            "replay number rollover",
            "replay timer timeout",
            "advisory non-fatal"
        };

        private static readonly string[] UncorrectableBits =
        {
            "data link protocol error",
            "poisoned TLP",
            "flow control protocol error",
            "completion timeout",
            "completer abort",
            "unexpected completion",
            "ACS violation",
            "uncorrectable internal error",
            "MCRC error",
            "receiver buffer overflow",
            "malformed TLP",
            "ECRC error"
        };

        private sealed class WheaEvent
        {
            public DateTime Time;
            public int Id;
            public byte Level;
            public Dictionary<string, string> Data;
        }

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name);

            List<WheaEvent> events = ReadEvents(context);
            if (events == null)
            {
                result.Warn("Cannot read WHEA events from the System log",
                    "run from an elevated prompt to read protected logs");
                return result;
            }

            if (events.Count == 0)
            {
                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "No WHEA events in the last {0} day(s).", context.Options.WindowDays));
                return result;
            }

            var corrected = 0;
            var uncorrected = 0;
            var byId = new Dictionary<int, int>();
            var byDay = new Dictionary<string, int>();
            var details = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in events)
            {
                var uncorrectedFlag = HasSetBit(item.Data, "UncorrectableErrorStatus");
                if (uncorrectedFlag) uncorrected++; else corrected++;

                Increment(byId, item.Id);
                Increment(byDay, item.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                var signature = Describe(item);
                Increment(details, signature);
            }

            var peak = PeakPerMinute(events);
            var recent = 0;
            var edge = DateTime.Now - TimeSpan.FromSeconds(60);
            foreach (var item in events)
            {
                if (item.Time >= edge) recent++;
            }

            result.Note(string.Format(CultureInfo.InvariantCulture,
                "WHEA events in window: {0} ({1} correctable, {2} uncorrected)", events.Count, corrected, uncorrected));
            result.Note("Per day: " + FormatDayCounts(byDay, 8));
            result.Note("Event ids: " + FormatEventIds(byId));
            result.Note(string.Format(CultureInfo.InvariantCulture,
                "Peak: {0} events in one minute; {1} events in the last 60 seconds",
                peak.Count, recent));

            foreach (var pair in details)
            {
                result.Note(pair.Value + "x " + pair.Key);
            }

            var top = string.Empty;
            var topCount = 0;
            foreach (var pair in details)
            {
                if (top.Length == 0 || pair.Value > topCount)
                {
                    topCount = pair.Value;
                    top = pair.Key;
                }
            }

            if (uncorrected > 0)
            {
                result.Fail(string.Format(CultureInfo.InvariantCulture,
                    "{0} uncorrected hardware error record(s) in the window", uncorrected),
                    "uncorrected errors mean work was lost; check power supply, memory and board firmware");
            }

            if (peak.Count >= 600)
            {
                result.Fail(string.Format(CultureInfo.InvariantCulture,
                    "WHEA error storm: {0} events/min at {1:HH:mm:ss} ({2:0.0} per second)",
                    peak.Count, peak.Time, peak.Count / 60.0),
                    top + " - sustained correctable link errors cause stutter; update firmware or force the PCIe link to Gen4");
            }
            else if (events.Count > 0)
            {
                result.Warn(string.Format(CultureInfo.InvariantCulture,
                    "{0} hardware error record(s) in the last {1} day(s)", events.Count, context.Options.WindowDays),
                    top + " - peak " + peak.Count + "/min at " +
                    peak.Time.ToString("HH:mm:ss") + "; if these come from the GPU root port, update firmware or force Gen4");
            }

            return result;
        }

        private static List<WheaEvent> ReadEvents(AuditContext context)
        {
            var filter = string.Format(CultureInfo.InvariantCulture,
                "*[System[Provider[@Name='{0}'] and TimeCreated[timediff(@SystemTime) <= {1}]]]",
                Provider,
                (long)context.Window.TotalMilliseconds);

            var events = new List<WheaEvent>();

            try
            {
                var query = new EventLogQuery("System", PathType.LogName, filter);

                using (var reader = new EventLogReader(query))
                {
                    EventRecord record;
                    while ((record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            events.Add(Parse(record));
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

        private static WheaEvent Parse(EventRecord record)
        {
            var item = new WheaEvent();
            item.Time = record.TimeCreated ?? DateTime.MinValue;
            item.Id = record.Id;
            item.Level = record.Level.HasValue ? record.Level.Value : (byte)0;
            item.Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                    if (key.Length > 0) item.Data[key] = element.InnerText;
                }
            }
            catch (XmlException)
            {
                // Event payload is not XML; the raw fields stay empty.
            }

            return item;
        }

        private static string Describe(WheaEvent item)
        {
            var parts = new List<string>();

            var corrected = DecodeBits(Text(item.Data, "CorrectableErrorStatus"), CorrectableBits);
            if (corrected != null) parts.Add("corrected: " + corrected);

            var uncorrected = DecodeBits(Text(item.Data, "UncorrectableErrorStatus"), UncorrectableBits);
            if (uncorrected != null) parts.Add("uncorrected: " + uncorrected);

            var device = DescribeDevice(item.Data);
            if (device.Length > 0) parts.Add(device);

            if (parts.Count == 0)
            {
                parts.Add("event id " + item.Id + " without PCIe detail");
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string DescribeDevice(IDictionary<string, string> data)
        {
            var primary = Text(data, "PrimaryDeviceName");
            if (primary.Length == 0) primary = Text(data, "Component");
            if (primary.Length == 0) primary = Text(data, "Device");

            var bus = Text(data, "Bus");
            var device = Text(data, "Device");
            var function = Text(data, "Function");
            var vendor = Text(data, "VendorId");
            var id = Text(data, "DeviceId");

            var parts = new List<string>();
            if (primary.Length > 0) parts.Add(primary);
            if (bus.Length > 0 && device.Length > 0 && function.Length > 0)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "bus {0}:{1}.{2}", bus, device, function));
            }

            if (vendor.Length > 0 || id.Length > 0)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "VEN_{0} DEV_{1}", vendor, id));
            }

            var source = Text(data, "ErrorSource");
            if (source.Length > 0) parts.Add("error source " + source);

            return string.Join(" ", parts.ToArray());
        }

        private static string DecodeBits(string raw, string[] names)
        {
            ulong bits;
            if (!TryParseBits(raw, out bits)) return null;
            if (bits == 0) return null;

            var parts = new List<string>();

            for (var index = 0; index < names.Length; index++)
            {
                if ((bits & (1UL << index)) != 0) parts.Add(names[index]);
            }

            var known = names.Length >= 64 ? ulong.MaxValue : (1UL << names.Length) - 1;
            var extra = bits & ~known;
            if (extra != 0) parts.Add("0x" + extra.ToString("X"));

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "0x" + bits.ToString("X");
        }

        private static bool TryParseBits(string raw, out ulong bits)
        {
            bits = 0;
            if (string.IsNullOrEmpty(raw)) return false;

            var text = raw.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);

            return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bits) ||
                   ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bits);
        }

        private static bool HasSetBit(IDictionary<string, string> data, string property)
        {
            ulong bits;
            return TryParseBits(Text(data, property), out bits) && bits != 0;
        }

        private static string Text(IDictionary<string, string> data, string property)
        {
            string value;
            if (data.TryGetValue(property, out value) && value != null) return value.Trim();
            return string.Empty;
        }

        private static Peak PeakPerMinute(List<WheaEvent> events)
        {
            var buckets = new Dictionary<DateTime, int>();

            foreach (var item in events)
            {
                var minute = new DateTime(item.Time.Year, item.Time.Month, item.Time.Day,
                    item.Time.Hour, item.Time.Minute, 0, item.Time.Kind);

                int current;
                buckets.TryGetValue(minute, out current);
                buckets[minute] = current + 1;
            }

            var peak = new Peak();
            foreach (var pair in buckets)
            {
                if (pair.Value > peak.Count)
                {
                    peak.Count = pair.Value;
                    peak.Time = pair.Key;
                }
            }

            return peak;
        }

        private sealed class Peak
        {
            public int Count;
            public DateTime Time;
        }

        private static void Increment(Dictionary<string, int> counters, string key)
        {
            int current;
            counters.TryGetValue(key, out current);
            counters[key] = current + 1;
        }

        private static void Increment(Dictionary<int, int> counters, int key)
        {
            int current;
            counters.TryGetValue(key, out current);
            counters[key] = current + 1;
        }

        private static string FormatDayCounts(Dictionary<string, int> counters, int limit)
        {
            var keys = new List<string>(counters.Keys);
            keys.Sort(StringComparer.Ordinal);

            var parts = new List<string>();
            for (var index = 0; index < keys.Count && index < limit; index++)
            {
                parts.Add(keys[index] + "=" + counters[keys[index]]);
            }

            if (keys.Count > limit) parts.Add("...");
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none";
        }

        private static string FormatEventIds(Dictionary<int, int> counters)
        {
            var parts = new List<string>();
            foreach (var pair in counters)
            {
                parts.Add(pair.Key + "x" + pair.Value);
            }

            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none";
        }
    }
}
