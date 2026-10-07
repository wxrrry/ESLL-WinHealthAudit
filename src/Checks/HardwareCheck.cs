using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class HardwareCheck : IHealthCheck
    {
        public string Name { get { return "Hardware"; } }

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name);
            DescribeProcessor(result);
            DescribeMemory(result);
            DescribeBoardAndBios(result);
            DescribeVideoControllers(result);
            return result;
        }

        private static void DescribeProcessor(CheckResult result)
        {
            var cpus = Wmi.Query("root\\cimv2",
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed, " +
                "LoadPercentage, Family FROM Win32_Processor");

            foreach (var cpu in cpus)
            {
                var name = Wmi.GetString(cpu, "Name");
                var cores = Wmi.GetNumber(cpu, "NumberOfCores");
                var threads = Wmi.GetNumber(cpu, "NumberOfLogicalProcessors");
                var rated = Wmi.GetNumber(cpu, "MaxClockSpeed");
                var current = Wmi.GetNumber(cpu, "CurrentClockSpeed");
                var load = Wmi.GetNumber(cpu, "LoadPercentage");

                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "CPU: {0} ({1} cores / {2} threads), {3} MHz rated, {4} MHz now, {5}% load snapshot",
                    name.Trim(), cores, threads, rated, current, load));

                if (rated > 0 && current > 0 && current * 100 < rated * 90)
                {
                    result.Note(string.Format(CultureInfo.InvariantCulture,
                        "CPU sits below its rated clock ({0} of {1} MHz) - this is a single snapshot, not proof of throttling.",
                        current, rated));
                }
            }
        }

        private static void DescribeMemory(CheckResult result)
        {
            var modules = Wmi.Query("root\\cimv2",
                "SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, DeviceLocator " +
                "FROM Win32_PhysicalMemory");

            var total = 0UL;
            var ratedSpeed = 0UL;
            var configuredSpeed = 0UL;
            var labels = new List<string>();

            foreach (var module in modules)
            {
                total += Wmi.GetNumber(module, "Capacity");
                ratedSpeed = Math.Max(ratedSpeed, Wmi.GetNumber(module, "Speed"));
                configuredSpeed = Math.Max(configuredSpeed, Wmi.GetNumber(module, "ConfiguredClockSpeed"));

                var part = Wmi.GetString(module, "PartNumber");
                if (part.Length > 0) labels.Add(part.Trim());
            }

            if (modules.Count > 0)
            {
                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "Memory: {0} in {1} module(s), rated {2} MT/s, configured {3} MT/s",
                    Format.Bytes(total), modules.Count, ratedSpeed, configuredSpeed));

                if (ratedSpeed > 0 && configuredSpeed > 0 && configuredSpeed * 100 < ratedSpeed * 95)
                {
                    result.Warn("Memory runs below its rated speed",
                        string.Format(CultureInfo.InvariantCulture,
                            "configured {0} MT/s vs rated {1} MT/s - enable the memory profile in firmware if you want the rated speed",
                            configuredSpeed, ratedSpeed));
                }
            }
        }

        private static void DescribeBoardAndBios(CheckResult result)
        {
            var board = Wmi.First("root\\cimv2", "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard");
            var bios = Wmi.First("root\\cimv2",
                "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SoftwareElementID FROM Win32_BIOS");

            result.Note("Board: " + Wmi.GetString(board, "Manufacturer") + " " + Wmi.GetString(board, "Product"));

            var release = Wmi.GetTime(bios, "ReleaseDate");
            var version = Wmi.GetString(bios, "SMBIOSBIOSVersion");

            if (release.HasValue)
            {
                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "Firmware: {0}, released {1:yyyy-MM-dd} ({2})", version, release.Value,
                    Format.TimeAgo(release.Value, DateTime.Now)));

                if (DateTime.Now - release.Value > TimeSpan.FromDays(365 * 2))
                {
                    result.Warn("Board firmware is older than two years",
                        string.Format(CultureInfo.InvariantCulture,
                            "v{0} from {1:yyyy-MM-dd} - new firmware revisions usually fix PCIe link and memory training issues",
                            version, release.Value));
                }
            }
            else
            {
                result.Note("Firmware: " + version);
            }
        }

        private static void DescribeVideoControllers(CheckResult result)
        {
            var controllers = Wmi.Query("root\\cimv2",
                "SELECT Name, DriverVersion, DriverDate, AdapterRAM, CurrentRefreshRate, VideoModeDescription, " +
                "Status, PNPDeviceID, VideoProcessor FROM Win32_VideoController");

            foreach (var controller in controllers)
            {
                var name = Wmi.GetString(controller, "Name");
                var pnp = Wmi.GetString(controller, "PNPDeviceID");
                var driverDate = Wmi.GetTime(controller, "DriverDate");
                var status = Wmi.GetString(controller, "Status");
                var refresh = Wmi.GetNumber(controller, "CurrentRefreshRate");

                var dedicated = ReadVideoMemory(pnp);
                if (dedicated == 0) dedicated = Wmi.GetNumber(controller, "AdapterRAM");

                var processor = Wmi.GetString(controller, "VideoProcessor");
                if (string.Equals(processor, name, StringComparison.OrdinalIgnoreCase)) processor = string.Empty;

                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "GPU: {0}{1}, driver {2}, {3} Hz, VRAM {4}",
                    name,
                    processor.Length > 0 ? " (" + processor + ")" : string.Empty,
                    driverDate.HasValue ? driverDate.Value.ToString("yyyy-MM-dd") : Wmi.GetString(controller, "DriverVersion"),
                    refresh,
                    dedicated > 0 ? Format.Bytes(dedicated) : "unknown"));

                if (status.Length > 0 && !string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    result.Warn("Video adapter reports status '" + status + "'", pnp);
                }

                if (driverDate.HasValue && DateTime.Now - driverDate.Value > TimeSpan.FromDays(540))
                {
                    result.Warn("Video driver is more than 18 months old",
                        string.Format(CultureInfo.InvariantCulture, "{0}: driver dated {1:yyyy-MM-dd}",
                            name, driverDate.Value));
                }
            }
        }

        private static ulong ReadVideoMemory(string pnpDeviceId)
        {
            try
            {
                using (var video = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Video"))
                {
                    if (video == null) return 0;

                    foreach (var guidName in video.GetSubKeyNames())
                    {
                        using (var guidKey = video.OpenSubKey(guidName))
                        {
                            if (guidKey == null) continue;

                            using (var adapter = guidKey.OpenSubKey("0000"))
                            {
                                if (adapter == null) continue;

                                var matching = Convert.ToString(adapter.GetValue("MatchingDeviceId") ?? string.Empty);
                                if (pnpDeviceId.Length > 0 && matching.Length > 0 &&
                                    pnpDeviceId.IndexOf(matching, StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    continue;
                                }

                                var size = ReadRegistrySize(adapter);
                                if (size > 0) return size;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Registry access can be denied; the caller falls back to WMI.
            }

            return 0;
        }

        private static ulong ReadRegistrySize(RegistryKey adapter)
        {
            var quoted = adapter.GetValue("HardwareInformation.qwMemorySize");

            var bytes = quoted as byte[];
            if (bytes != null)
            {
                if (bytes.Length >= 8) return BitConverter.ToUInt64(bytes, 0);
                if (bytes.Length >= 4) return BitConverter.ToUInt32(bytes, 0);
            }

            if (quoted is long) return (ulong)(long)quoted;
            if (quoted is int) return unchecked((uint)Convert.ToInt32(quoted));

            var legacy = adapter.GetValue("HardwareInformation.AdapterRAM");
            if (legacy is int) return unchecked((uint)Convert.ToInt32(legacy));

            return 0;
        }
    }
}
