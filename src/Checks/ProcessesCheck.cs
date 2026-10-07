using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class ProcessesCheck : IHealthCheck
    {
        public string Name { get { return "Processes, services and startup"; } }

        private const int SampleMilliseconds = 400;

        private sealed class Sample
        {
            public string Name;
            public int Id;
            public long WorkingSet;
            public TimeSpan Cpu;
        }

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name);

            var first = Snapshot();
            Thread.Sleep(SampleMilliseconds);
            var second = Snapshot();

            DescribeProcesses(result, first, second);
            DescribeServices(result);
            DescribeStartup(result);

            return result;
        }

        private static List<Sample> Snapshot()
        {
            var samples = new List<Sample>();

            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        var sample = new Sample();
                        sample.Name = process.ProcessName;
                        sample.Id = process.Id;
                        sample.WorkingSet = process.WorkingSet64;
                        sample.Cpu = SafeCpu(process);
                        samples.Add(sample);
                    }
                    catch (Exception)
                    {
                        // The process exited or belongs to another user.
                    }
                }
            }

            return samples;
        }

        private static TimeSpan SafeCpu(Process process)
        {
            try
            {
                return process.TotalProcessorTime;
            }
            catch (Exception)
            {
                return TimeSpan.Zero;
            }
        }

        private static void DescribeProcesses(CheckResult result, List<Sample> first, List<Sample> second)
        {
            var latest = new Dictionary<int, Sample>();
            foreach (var sample in second) latest[sample.Id] = sample;

            var measured = new Dictionary<int, Sample>();
            foreach (var sample in first)
            {
                if (latest.ContainsKey(sample.Id)) measured[sample.Id] = sample;
            }

            result.Note(second.Count + " processes running at sampling time.");

            var byMemory = new List<Sample>(second);
            byMemory.Sort(delegate(Sample left, Sample right) { return right.WorkingSet.CompareTo(left.WorkingSet); });

            var topMemory = new StringBuilder();
            for (var index = 0; index < byMemory.Count && index < 8; index++)
            {
                var sample = byMemory[index];
                topMemory.Append(sample.Name);
                topMemory.Append(" ");
                topMemory.Append(Format.Bytes((ulong)sample.WorkingSet));
                if (index < 7) topMemory.Append(", ");
            }

            if (topMemory.Length > 0) result.Note("Top by memory: " + topMemory);

            var cpu = new List<KeyValuePair<string, double>>();
            foreach (var pair in measured)
            {
                var delta = latest[pair.Key].Cpu - pair.Value.Cpu;
                if (delta <= TimeSpan.Zero) continue;

                var percent = delta.TotalMilliseconds * 100.0 /
                              (SampleMilliseconds * Math.Max(1, Environment.ProcessorCount));

                cpu.Add(new KeyValuePair<string, double>(latest[pair.Key].Name, percent));
            }

            cpu.Sort(delegate(KeyValuePair<string, double> left, KeyValuePair<string, double> right)
            {
                return right.Value.CompareTo(left.Value);
            });

            var topCpu = new StringBuilder();
            for (var index = 0; index < cpu.Count && index < 8; index++)
            {
                topCpu.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.#}%", cpu[index].Value));
                topCpu.Append(" ");
                topCpu.Append(cpu[index].Key);
                if (index < 7) topCpu.Append(", ");
            }

            if (topCpu.Length > 0) result.Note("Top by CPU during the sample: " + topCpu);
        }

        private static void DescribeServices(CheckResult result)
        {
            ServiceController[] services;

            try
            {
                services = ServiceController.GetServices();
            }
            catch (Exception ex)
            {
                result.Note("Service list unavailable: " + ex.Message);
                return;
            }

            var running = 0;
            var stopped = new List<string>();

            foreach (var service in services)
            {
                using (service)
                {
                    try
                    {
                        if (service.Status == ServiceControllerStatus.Running) running++;
                        if (service.StartType == ServiceStartMode.Automatic &&
                            service.Status == ServiceControllerStatus.Stopped)
                        {
                            stopped.Add(service.ServiceName);
                        }
                    }
                    catch (Exception)
                    {
                        // The service vanished between the enumeration and the query.
                    }
                }
            }

            result.Note(services.Length + " services installed, " + running + " running.");

            if (stopped.Count > 0)
            {
                result.Warn(stopped.Count + " automatic service(s) are stopped",
                    Join(stopped, 10));
            }
        }

        private static void DescribeStartup(CheckResult result)
        {
            var entries = new List<string>();
            var locations = new[]
            {
                "Software\\Microsoft\\Windows\\CurrentVersion\\Run",
                "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
                "Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Run"
            };

            CollectRunEntries(Registry.CurrentUser, locations, entries);
            CollectRunEntries(Registry.LocalMachine, locations, entries);

            result.Note(entries.Count + " startup entry/entries: " + Join(entries, 20));
        }

        private static void CollectRunEntries(RegistryKey root, string[] locations, List<string> entries)
        {
            foreach (var location in locations)
            {
                try
                {
                    using (var key = root.OpenSubKey(location))
                    {
                        if (key == null) continue;

                        foreach (var name in key.GetValueNames())
                        {
                            entries.Add((root.Name.EndsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
                                ? "HKCU\\"
                                : "HKLM\\") + location + " -> " + name);
                        }
                    }
                }
                catch (Exception)
                {
                    // A protected key simply does not open without elevation.
                }
            }
        }


        private static string Join(List<string> values, int limit)
        {
            var parts = new List<string>();
            for (var index = 0; index < values.Count && index < limit; index++)
            {
                parts.Add(values[index]);
            }

            if (values.Count > limit) parts.Add("...");
            return string.Join(", ", parts.ToArray());
        }
    }
}
