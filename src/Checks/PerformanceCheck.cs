using System;
using System.Collections.Generic;
using System.Globalization;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class PerformanceCheck : IHealthCheck
    {
        public string Name { get { return "Performance counters"; } }

        private const string Scope = "root\\cimv2";

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name);

            DescribeCpu(result);
            DescribeMemory(result);
            DescribeSystem(result);

            return result;
        }

        private static void DescribeCpu(CheckResult result)
        {
            List<Dictionary<string, object>> rows;

            try
            {
                rows = Wmi.Query(Scope,
                    "SELECT Name, PercentProcessorTime, PercentDPCTime, PercentInterruptTime, " +
                    "DPCsQueuedPersec, InterruptsPersec FROM Win32_PerfFormattedData_PerfOS_Processor");
            }
            catch (Exception ex)
            {
                result.Note("CPU counters unavailable: " + ex.Message);
                return;
            }

            foreach (var row in rows)
            {
                var name = Wmi.GetString(row, "Name");
                var total = Wmi.GetNumber(row, "PercentProcessorTime");
                var dpc = Wmi.GetNumber(row, "PercentDPCTime");
                var interrupt = Wmi.GetNumber(row, "PercentInterruptTime");

                if (name == "_Total")
                {
                    result.Note(string.Format(CultureInfo.InvariantCulture,
                        "CPU total: {0}% busy at sampling time", total));

                    if (total >= 95)
                    {
                        result.Warn("CPU was fully saturated at sampling time", total + "% busy on all logical processors");
                    }

                    continue;
                }

                if (dpc >= 15)
                {
                    result.Warn(string.Format(CultureInfo.InvariantCulture,
                        "Deferred procedure calls take {0}% of logical processor {1}", dpc, name),
                        "a driver monopolises the core - this shows up as input lag and frame spikes");
                }

                if (interrupt >= 15)
                {
                    result.Warn(string.Format(CultureInfo.InvariantCulture,
                        "Interrupts take {0}% of logical processor {1}", interrupt, name),
                        "interrupt storms come from drivers and peripherals");
                }

                if (dpc > 0 || interrupt > 0)
                {
                    result.Note(string.Format(CultureInfo.InvariantCulture,
                        "CPU {0}: DPC {1}%, interrupt {2}%", name, dpc, interrupt));
                }
            }
        }

        private static void DescribeMemory(CheckResult result)
        {
            Dictionary<string, object> row;

            try
            {
                row = Wmi.First(Scope,
                    "SELECT AvailableBytes, PoolPagedBytes, PoolNonpagedBytes, CacheBytes, " +
                    "PageFaultsPersec, PagesInputPersec FROM Win32_PerfFormattedData_PerfOS_Memory");
            }
            catch (Exception ex)
            {
                result.Note("Memory counters unavailable: " + ex.Message);
                return;
            }

            var available = Wmi.GetNumber(row, "AvailableBytes");
            var paged = Wmi.GetNumber(row, "PoolPagedBytes");
            var nonPaged = Wmi.GetNumber(row, "PoolNonpagedBytes");
            var cache = Wmi.GetNumber(row, "CacheBytes");
            var faults = Wmi.GetNumber(row, "PageFaultsPersec");
            var pageIns = Wmi.GetNumber(row, "PagesInputPersec");

            var total = TotalPhysicalMemory();

            result.Note(string.Format(CultureInfo.InvariantCulture,
                "Memory: {0} available, cache {1}, paged pool {2}, non-paged pool {3}",
                Format.Bytes(available), Format.Bytes(cache), Format.Bytes(paged), Format.Bytes(nonPaged)));
            result.Note(string.Format(CultureInfo.InvariantCulture,
                "Page faults {0}/s, pages read from disk {1}/s", faults, pageIns));

            if (total > 0 && available * 100 < total * 8)
            {
                result.Warn("Little free memory left",
                    string.Format(CultureInfo.InvariantCulture, "{0} of {1} available",
                        Format.Bytes(available), Format.Bytes(total)));
            }

            if (nonPaged > 1024UL * 1024 * 1024)
            {
                result.Warn("Non-paged pool is larger than 1 GB",
                    Format.Bytes(nonPaged) + " - a driver is probably leaking kernel memory");
            }

            if (pageIns > 1000)
            {
                result.Warn("The system is paging heavily", pageIns + " pages/s read from disk");
            }
        }

        private static void DescribeSystem(CheckResult result)
        {
            Dictionary<string, object> row;

            try
            {
                row = Wmi.First(Scope,
                    "SELECT ProcessorQueueLength, ContextSwitchesPersec, SystemCallsPersec, Threads, Processes " +
                    "FROM Win32_PerfFormattedData_PerfOS_System");
            }
            catch (Exception ex)
            {
                result.Note("System counters unavailable: " + ex.Message);
                return;
            }

            var queue = Wmi.GetNumber(row, "ProcessorQueueLength");
            var switches = Wmi.GetNumber(row, "ContextSwitchesPersec");
            var threads = Wmi.GetNumber(row, "Threads");
            var processes = Wmi.GetNumber(row, "Processes");

            result.Note(string.Format(CultureInfo.InvariantCulture,
                "System: {0} processes, {1} threads, processor queue {2}, {3} context switches/s",
                processes, threads, queue, switches));

            if (queue >= 5)
            {
                result.Warn("Threads are waiting for a free CPU", "processor queue length is " + queue);
            }
        }

        private static ulong TotalPhysicalMemory()
        {
            var computer = Wmi.First(Scope, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            return Wmi.GetNumber(computer, "TotalPhysicalMemory");
        }
    }
}
