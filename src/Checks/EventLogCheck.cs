using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace WinHealthAudit.Checks
{
    public sealed class EventLogCheck : IHealthCheck
    {
        public string Name { get { return "Event logs"; } }

        private const string WheaProvider = "Microsoft-Windows-WHEA-Logger";
        private const int MaxRecordsPerLog = 50000;
        private const int MaxFindings = 25;

        private sealed class Rule
        {
            public Rule(Severity severity, string message)
            {
                Severity = severity;
                Message = message;
            }

            public Severity Severity;
            public string Message;
        }

        private sealed class Counter
        {
            public Counter()
            {
                Count = 0;
                SampleTime = DateTime.MinValue;
            }

            public int Count;
            public DateTime SampleTime;
        }

        private static readonly Dictionary<string, Rule> Rules = BuildRules();

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name);
            var groups = new Dictionary<string, Counter>(StringComparer.OrdinalIgnoreCase);
            var logSummaries = new List<string>();
            var skipped = new List<string>();
            var truncated = false;
            var wheaEvents = 0;
            var systemErrors = 0;
            var windowMs = (long)context.Window.TotalMilliseconds;

            foreach (var logName in SortedLogNames())
            {
                EventLogConfiguration config;
                try
                {
                    config = new EventLogConfiguration(logName);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!config.IsEnabled) continue;

                var count = ReadLog(logName, windowMs, groups, ref truncated);
                var isSystem = string.Equals(logName, "System", StringComparison.OrdinalIgnoreCase);

                if (isSystem)
                {
                    systemErrors = count.Total;
                    wheaEvents = count.Whea;
                }

                if (count.Total > 0)
                {
                    logSummaries.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}: {1} error event(s){2}", logName, count.Total,
                        count.Whea > 0 ? " (" + count.Whea + " of them WHEA)" : string.Empty));
                }

                WarnWhenLogIsFull(result, logName, config);
            }

            foreach (var summary in logSummaries)
            {
                result.Note(summary);
            }

            if (skipped.Count > 0)
            {
                result.Note("Logs skipped: " + string.Join(", ", skipped.ToArray()));
            }

            if (truncated)
            {
                result.Note("At least one log hit the 50000 event read limit; counts for it are a lower bound.");
            }

            if (systemErrors > 0 && wheaEvents > systemErrors * 0.3)
            {
                result.Warn("The System log is dominated by WHEA hardware errors",
                    string.Format(CultureInfo.InvariantCulture,
                        "{0} of {1} error events are WHEA - other problems may have been rotated out of the log",
                        wheaEvents, systemErrors));
            }

            AppendFindings(result, groups);
            return result;
        }

        private static void WarnWhenLogIsFull(CheckResult result, string logName, EventLogConfiguration config)
        {
            if (config.MaximumSizeInBytes <= 0) return;
            if (!IsImportantLog(logName)) return;

            try
            {
                var info = EventLogSession.GlobalSession.GetLogInformation(logName, PathType.LogName);
                if (info.FileSize < config.MaximumSizeInBytes * 0.9) return;

                result.Warn(string.Format(CultureInfo.InvariantCulture,
                    "Log '{0}' is {1:0}% full and will overwrite its oldest events",
                    logName, (double)info.FileSize / config.MaximumSizeInBytes * 100),
                    string.Format(CultureInfo.InvariantCulture, "{0} of {1} bytes",
                        info.FileSize, config.MaximumSizeInBytes));
            }
            catch (Exception)
            {
                // Some logs refuse to report their size; skip the warning.
            }
        }

        /// <summary>
        /// Most channels sit at their size cap by design and recycle silently. Only the
        /// logs people actually read when they investigate a problem are worth flagging.
        /// </summary>
        private static bool IsImportantLog(string logName)
        {
            if (logName.IndexOf("WHEA", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            switch (logName)
            {
                case "System":
                case "Application":
                case "Security":
                case "Setup":
                    return true;
                default:
                    return false;
            }
        }

        private sealed class LogCount
        {
            public int Total;
            public int Whea;
        }

        private static LogCount ReadLog(string logName, long windowMs,
            Dictionary<string, Counter> groups, ref bool truncated)
        {
            var count = new LogCount();
            var filter = string.Format(CultureInfo.InvariantCulture,
                "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {0}]]]", windowMs);

            try
            {
                var query = new EventLogQuery(logName, PathType.LogName, filter);

                using (var reader = new EventLogReader(query))
                {
                    var read = 0;

                    EventRecord record;
                    while ((record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            var provider = record.ProviderName ?? string.Empty;
                            var id = record.Id;
                            var time = record.TimeCreated ?? DateTime.MinValue;

                            count.Total++;
                            if (string.Equals(provider, WheaProvider, StringComparison.OrdinalIgnoreCase))
                            {
                                count.Whea++;
                            }
                            else
                            {
                                Add(groups, provider, id, time);
                            }
                        }

                        read++;
                        if (read >= MaxRecordsPerLog)
                        {
                            truncated = true;
                            break;
                        }
                    }
                }
            }
            catch (EventLogException)
            {
                count.Total = 0;
                count.Whea = 0;
            }
            catch (UnauthorizedAccessException)
            {
                count.Total = 0;
                count.Whea = 0;
            }

            return count;
        }

        private static void Add(Dictionary<string, Counter> groups, string provider, int id, DateTime time)
        {
            var key = provider + "/" + id;

            Counter counter;
            if (!groups.TryGetValue(key, out counter))
            {
                counter = new Counter();
                groups[key] = counter;
            }

            counter.Count++;
            if (time > counter.SampleTime) counter.SampleTime = time;
        }

        private static void AppendFindings(CheckResult result, Dictionary<string, Counter> groups)
        {
            var ordered = new List<KeyValuePair<string, Counter>>(groups);
            ordered.Sort(Compare);

            var hidden = 0;

            for (var index = 0; index < ordered.Count; index++)
            {
                var pair = ordered[index];
                var key = pair.Key;
                var counter = pair.Value;

                Rule rule;
                var classified = Rules.TryGetValue(key, out rule);
                if (!classified)
                {
                    // Without a rule the event is only worth reporting once it repeats.
                    var severity = counter.Count >= 5 ? Severity.Warning : Severity.Info;
                    rule = new Rule(severity, "repeated errors from " + key);
                }

                if (rule.Severity == Severity.Info)
                {
                    continue;
                }

                if (index >= MaxFindings)
                {
                    hidden++;
                    continue;
                }

                var message = string.Format(CultureInfo.InvariantCulture,
                    "{0} - {1}", counter.Count, rule.Message);
                var seen = counter.SampleTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                var evidence = classified ? key + " last seen " + seen : "last seen " + seen;

                if (rule.Severity == Severity.Critical) result.Fail(message, evidence);
                else result.Warn(message, evidence);
            }

            if (hidden > 0)
            {
                result.Note(hidden + " further repeated error group(s) are listed in the report table only.");
            }
        }

        private static int Compare(KeyValuePair<string, Counter> left, KeyValuePair<string, Counter> right)
        {
            var leftSeverity = SeverityFor(left.Key, left.Value);
            var rightSeverity = SeverityFor(right.Key, right.Value);

            if (leftSeverity != rightSeverity) return (int)rightSeverity - (int)leftSeverity;
            return right.Value.Count - left.Value.Count;
        }

        private static Severity SeverityFor(string key, Counter counter)
        {
            Rule rule;
            if (Rules.TryGetValue(key, out rule)) return rule.Severity;
            return counter.Count >= 5 ? Severity.Warning : Severity.Info;
        }

        private static string[] SortedLogNames()
        {
            var names = new List<string>(EventLogSession.GlobalSession.GetLogNames());
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        private static Dictionary<string, Rule> BuildRules()
        {
            var rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);

            AddRule(rules, "Ntfs/147", Severity.Critical,
                "storage request hung - NTFS waited more than 30 seconds for the disk");
            AddRule(rules, "Ntfs/55", Severity.Warning, "data could not be flushed to disk in time");
            AddRule(rules, "disk/7", Severity.Critical, "device error - the controller did not respond");
            AddRule(rules, "disk/11", Severity.Critical, "device error - the controller reset the drive");
            AddRule(rules, "disk/129", Severity.Critical, "the storage link to the drive was reset");
            AddRule(rules, "disk/153", Severity.Critical, "drive blocked while waiting for a reset");
            AddRule(rules, "storahci/129", Severity.Critical, "the storage driver reset the device");
            AddRule(rules, "stornvme/129", Severity.Critical, "the NVMe controller reset the device");

            AddRule(rules, "Microsoft-Windows-Kernel-PnP/219", Severity.Critical,
                "a driver failed to load for a device");
            AddRule(rules, "Microsoft-Windows-Kernel-Power/41", Severity.Critical,
                "the last shutdown was not clean");
            AddRule(rules, "Microsoft-Windows-WER-SystemErrorReporting/1001", Severity.Critical,
                "bugcheck / blue screen");
            AddRule(rules, "Microsoft-Windows-Resource-Exhaustion-Detector/2004", Severity.Critical,
                "the system ran out of memory and terminated a process");

            AddRule(rules, "Application Error/1000", Severity.Warning, "application crashed");
            AddRule(rules, "Application Hang/1002", Severity.Warning, "application stopped responding");
            AddRule(rules, "Application Hang/1001", Severity.Warning, "application stopped responding");

            AddRule(rules, "Service Control Manager/7000", Severity.Warning, "a service failed to start");
            AddRule(rules, "Service Control Manager/7011", Severity.Warning, "service start timed out");
            AddRule(rules, "Service Control Manager/7022", Severity.Warning, "service hung while starting");
            AddRule(rules, "Service Control Manager/7026", Severity.Warning,
                "dependent services were disabled");
            AddRule(rules, "Service Control Manager/7031", Severity.Warning,
                "a service terminated unexpectedly");

            AddRule(rules, "Microsoft-Windows-Perflib/1008", Severity.Warning,
                "performance counter library is broken");
            AddRule(rules, "Microsoft-Windows-WindowsUpdateClient/25", Severity.Warning,
                "Windows Update check failed");
            AddRule(rules, "MsiInstaller/11708", Severity.Warning, "installer failed");
            AddRule(rules, "MsiInstaller/11724", Severity.Warning, "installer did not complete");
            AddRule(rules, "Microsoft-Windows-CAPI2/513", Severity.Info,
                "certificate chain validation failed");
            AddRule(rules, "Bonjour Service/100", Severity.Info, "multicast DNS name conflict");

            return rules;
        }

        private static void AddRule(Dictionary<string, Rule> rules, string key, Severity severity, string message)
        {
            rules[key] = new Rule(severity, message);
        }
    }
}
