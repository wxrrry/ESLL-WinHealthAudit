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
            public Rule(Severity severity, string message, string advice = null)
            {
                Severity = severity;
                Message = message;
                Advice = advice ?? string.Empty;
            }

            public Severity Severity;
            public string Message;
            public string Advice;
        }

        private sealed class Counter
        {
            public Counter()
            {
                Count = 0;
                SampleTime = DateTime.MinValue;
                Log = string.Empty;
                Provider = string.Empty;
                Id = 0;
            }

            public int Count;
            public DateTime SampleTime;
            public string Log;
            public string Provider;
            public int Id;
        }

        private static readonly Dictionary<string, Rule> Rules = BuildRules();

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name, "XPath-filtered error and critical records from the event logs over the selected window");
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
                        wheaEvents, systemErrors),
                    "events.whea-dominant",
                    "System log error records over the selected window, grouped by provider",
                    "fix the hardware errors first (see the WHEA check) - until they stop, other problems stay buried in the log");
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
                        info.FileSize, config.MaximumSizeInBytes),
                    "events.logfull",
                    "event log size from EventLogConfiguration (eventvwr > log > Properties)",
                    "raise the maximum log size or clear old records if you rely on this log for investigations");
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
                                Add(groups, logName, provider, id, time);
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

        private static void Add(Dictionary<string, Counter> groups, string logName, string provider, int id, DateTime time)
        {
            var key = provider + "/" + id;

            Counter counter;
            if (!groups.TryGetValue(key, out counter))
            {
                counter = new Counter();
                counter.Log = logName;
                counter.Provider = provider;
                counter.Id = id;
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
                    rule = new Rule(severity, "repeated errors from " + key,
                        "note the program or driver named in the event details, search for \"provider + event id\" online, then update or reinstall that component");
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
                var ruleId = classified ? key : "events.generic";
                var detail = string.Format(CultureInfo.InvariantCulture,
                    "{0} log - event ID {1} from provider \"{2}\"{3}",
                    counter.Log, counter.Id, counter.Provider,
                    classified ? ", matched a known-issue rule" : "");

                if (rule.Severity == Severity.Critical) result.Fail(message, evidence, ruleId, detail, rule.Advice);
                else result.Warn(message, evidence, ruleId, detail, rule.Advice);
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
                "storage request hung - NTFS waited more than 30 seconds for the disk",
                "back up important data and check S.M.A.R.T. health, cables and power - a hung storage request often precedes drive failure");
            AddRule(rules, "Ntfs/55", Severity.Warning, "data could not be flushed to disk in time",
                "slow writes under load - check the disk, its driver and heavy background activity; run chkdsk if it keeps repeating");
            AddRule(rules, "disk/7", Severity.Critical, "device error - the controller did not respond",
                "check SATA/NVMe cables and power, update the storage driver; if it recurs, back up and plan a drive replacement");
            AddRule(rules, "disk/11", Severity.Critical, "device error - the controller reset the drive",
                "test another cable and port, update the drive firmware; repeated resets usually mean a failing drive");
            AddRule(rules, "disk/129", Severity.Critical, "the storage link to the drive was reset",
                "reseat the drive, update chipset and storage drivers, then check S.M.A.R.T. afterward");
            AddRule(rules, "disk/153", Severity.Critical, "drive blocked while waiting for a reset",
                "update BIOS and chipset drivers and check the power supply; the controller is stalling under load");
            AddRule(rules, "storahci/129", Severity.Critical, "the storage driver reset the device",
                "update the chipset/storage driver and try another port or cable");
            AddRule(rules, "stornvme/129", Severity.Critical, "the NVMe controller reset the device",
                "update motherboard firmware and the NVMe driver, and check that the drive is not overheating");

            AddRule(rules, "Microsoft-Windows-Kernel-PnP/219", Severity.Critical,
                "a driver failed to load for a device",
                "open Device Manager, find the device with the warning and reinstall or roll back its driver");
            AddRule(rules, "Microsoft-Windows-Kernel-Power/41", Severity.Critical,
                "the last shutdown was not clean",
                "the machine lost power or crashed - check the PSU, temperatures and the events right before the shutdown");
            AddRule(rules, "Microsoft-Windows-WER-SystemErrorReporting/1001", Severity.Critical,
                "bugcheck / blue screen",
                "note the bugcheck code in the event details and search for it - it usually points at the failing driver");
            AddRule(rules, "Microsoft-Windows-Resource-Exhaustion-Detector/2004", Severity.Critical,
                "the system ran out of memory and terminated a process",
                "find the memory hog in Task Manager; if it repeats often, more RAM is needed");

            AddRule(rules, "Application Error/1000", Severity.Warning, "application crashed",
                "the event details name the faulting module - update or reinstall that program");
            AddRule(rules, "Application Hang/1002", Severity.Warning, "application stopped responding",
                "update the program; if it repeats, check its plugins and how much free memory is left");
            AddRule(rules, "Application Hang/1001", Severity.Warning, "application stopped responding",
                "update the program; if it repeats, check its plugins and how much free memory is left");

            AddRule(rules, "Service Control Manager/7000", Severity.Warning, "a service failed to start",
                "in services.msc check the service, its dependencies and its account; use Automatic (Delayed) if it races the boot");
            AddRule(rules, "Service Control Manager/7011", Severity.Warning, "service start timed out",
                "the service waits too long for a dependency or a slow disk - check services.msc and disk load");
            AddRule(rules, "Service Control Manager/7022", Severity.Warning, "service hung while starting",
                "a dependency or the disk is stalling the start - check which service it waits on");
            AddRule(rules, "Service Control Manager/7026", Severity.Warning,
                "dependent services were disabled",
                "re-enable the required dependencies in services.msc");
            AddRule(rules, "Service Control Manager/7031", Severity.Warning,
                "a service terminated unexpectedly",
                "the service crashed - update or reinstall it; the event details name the module");

            AddRule(rules, "Microsoft-Windows-Perflib/1008", Severity.Warning,
                "performance counter library is broken",
                "from an elevated prompt run: lodctr /R - it rebuilds the performance counter libraries");
            AddRule(rules, "Microsoft-Windows-WindowsUpdateClient/25", Severity.Warning,
                "Windows Update check failed",
                "run the Windows Update troubleshooter; if it persists, reset the update components");
            AddRule(rules, "MsiInstaller/11708", Severity.Warning, "installer failed",
                "re-run the installer as administrator and read the Application log for the return code");
            AddRule(rules, "MsiInstaller/11724", Severity.Warning, "installer did not complete",
                "re-run the installer as administrator; repair the product from Settings > Apps if needed");
            AddRule(rules, "Microsoft-Windows-CAPI2/513", Severity.Info,
                "certificate chain validation failed",
                "usually a missing root certificate or a wrong clock - check the system time and update Windows");
            AddRule(rules, "Bonjour Service/100", Severity.Info, "multicast DNS name conflict",
                "another device uses this network name - rename this PC in Settings > System > About");

            return rules;
        }

        private static void AddRule(Dictionary<string, Rule> rules, string key, Severity severity,
            string message, string advice)
        {
            rules[key] = new Rule(severity, message, advice);
        }
    }
}
