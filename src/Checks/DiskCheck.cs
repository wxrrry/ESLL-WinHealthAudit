using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class DiskCheck : IHealthCheck
    {
        public string Name { get { return "Disks and page file"; } }

        private static readonly Dictionary<ulong, string> HealthStates = BuildHealthStates();
        private static readonly Dictionary<ulong, string> BusTypes = BuildBusTypes();

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name, "WMI disk health, logical-disk free space, page file settings and I/O counters");

            DescribePhysicalDisks(result);
            DescribeVolumes(result);
            DescribePageFile(result, context);
            DescribeQueueLength(result);

            return result;
        }

        private static void DescribePhysicalDisks(CheckResult result)
        {
            var disks = new List<Dictionary<string, object>>();

            try
            {
                disks = Wmi.Query("root\\Microsoft\\Windows\\Storage",
                    "SELECT DeviceId, FriendlyName, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk");
            }
            catch (Exception ex)
            {
                result.Note("Storage class unavailable (" + ex.Message + "); falling back to Win32_DiskDrive.");
            }

            if (disks.Count == 0)
            {
                disks = Wmi.Query("root\\cimv2", "SELECT DeviceID, Model, Size, Status FROM Win32_DiskDrive");
            }

            foreach (var disk in disks)
            {
                var name = Wmi.GetString(disk, "FriendlyName");
                if (name.Length == 0) name = Wmi.GetString(disk, "Model");

                var size = Wmi.GetNumber(disk, "Size");
                var healthRaw = Wmi.GetString(disk, "HealthStatus");
                if (healthRaw.Length == 0) healthRaw = Wmi.GetString(disk, "Status");

                var health = DescribeHealth(healthRaw);
                var bus = DescribeBus(Wmi.GetString(disk, "BusType"));

                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "Disk {0}: {1}, {2}, health {3}, bus {4}",
                    Wmi.GetString(disk, "DeviceId"),
                    name,
                    size > 0 ? Format.Bytes(size) : "size unknown",
                    health,
                    bus));

                if (healthRaw == "1" || healthRaw == "Warning")
                {
                    result.Warn("Disk reports health 'Warning'", name + " (" + health + ")",
                        "disk.health-warning", "MSFT_Disk / physical disk health status via Storage Management WMI",
                        "back up important data now and plan a replacement - a disk that reports Warning is close to failing");
                }
                else if (healthRaw == "2" || healthRaw == "Unhealthy")
                {
                    result.Fail("Disk reports health 'Unhealthy'", name + " (" + health + ")",
                        "disk.health-unhealthy", "MSFT_Disk / physical disk health status via Storage Management WMI",
                        "immediate: back up your data from this disk and replace it - Unhealthy means failure is imminent");
                }
            }
        }

        private static string DescribeHealth(string raw)
        {
            switch (raw)
            {
                case "0":
                case "Healthy":
                case "OK":
                    return "healthy";
                case "1":
                case "Warning":
                    return "warning";
                case "2":
                case "Unhealthy":
                    return "unhealthy";
                default:
                    return raw.Length > 0 ? raw : "unknown";
            }
        }

        private static string DescribeBus(string raw)
        {
            ulong value;
            if (ulong.TryParse(raw, out value))
            {
                string name;
                if (BusTypes.TryGetValue(value, out name)) return name;
            }

            return raw.Length > 0 ? raw : "unknown";
        }

        private static Dictionary<ulong, string> BuildHealthStates()
        {
            var states = new Dictionary<ulong, string>();
            states[0] = "healthy";
            states[1] = "warning";
            states[2] = "unhealthy";
            return states;
        }

        private static Dictionary<ulong, string> BuildBusTypes()
        {
            var types = new Dictionary<ulong, string>();
            types[0] = "unknown";
            types[1] = "SCSI";
            types[2] = "ATAPI";
            types[3] = "ATA";
            types[4] = "IEEE1394";
            types[5] = "SSA";
            types[6] = "Fibre Channel";
            types[7] = "USB";
            types[8] = "RAID";
            types[9] = "iSCSI";
            types[10] = "SAS";
            types[11] = "SATA";
            types[12] = "SD";
            types[13] = "MMC";
            types[14] = "Virtual";
            types[15] = "File back";
            types[16] = "Spaces";
            types[17] = "NVMe";
            return types;
        }

        private static void DescribeVolumes(CheckResult result)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                if (!drive.IsReady) continue;

                var total = (ulong)drive.TotalSize;
                var free = (ulong)drive.AvailableFreeSpace;

                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "Volume {0} [{1}]: {2} total, {3} free ({4})",
                    drive.Name,
                    drive.DriveFormat,
                    Format.Bytes(total),
                    Format.Bytes(free),
                    Format.Percent(free, total)));

                if (total < 4UL * 1024 * 1024 * 1024) continue;

                var percent = total == 0 ? 100 : free * 100 / total;

                if (drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                {
                    result.Signal("cFree", total == 0 ? 100.0 : Math.Round(free * 100.0 / total, 1));
                }

                if (percent < 10)
                {
                    result.Fail(string.Format(CultureInfo.InvariantCulture,
                        "Volume {0} is almost full ({1} free)", drive.Name.TrimEnd('\\'), Format.Percent(free, total)),
                        "Windows needs free space for updates, paging and temporary files",
                        "disk.full", "Win32_LogicalDisk free space",
                        "free up at least 10-15% of this volume - move large files elsewhere or run Storage Sense (Settings > System > Storage)");
                }
                else if (percent < 20)
                {
                    result.Warn(string.Format(CultureInfo.InvariantCulture,
                        "Volume {0} is running low ({1} free)", drive.Name.TrimEnd('\\'), Format.Percent(free, total)),
                        "under 20% free",
                        "disk.low", "Win32_LogicalDisk free space",
                        "clear temporary files and move big data to another drive - below 20% free, updates and paging get slow");
                }
            }
        }

        private static void DescribePageFile(CheckResult result, AuditContext context)
        {
            string[] configured = null;
            string[] existing = null;
            var autoManaged = false;

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management"))
                {
                    if (key != null)
                    {
                        configured = key.GetValue("PagingFiles") as string[];
                        existing = key.GetValue("ExistingPageFiles") as string[];
                    }
                }

                var computer = Wmi.First("root\\cimv2", "SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem");
                autoManaged = Wmi.GetBool(computer, "AutomaticManagedPagefile");
            }
            catch (Exception ex)
            {
                result.Warn("Cannot read the page file configuration", ex.Message,
                    "disk.pagefile-unreadable", "Win32_PageFileSetting via WMI",
                    "run the audit from an elevated prompt so page file settings can be read");
            }

            result.Note("Page file: " + (autoManaged
                ? "managed automatically by Windows"
                : (configured != null && configured.Length > 0
                    ? JoinPaths(configured)
                    : "not configured")));

            if (existing != null && existing.Length > 0)
            {
                var listed = new List<string>();
                foreach (var path in existing) listed.Add(CleanPath(path));

                result.Note("Page files Windows is aware of: " + JoinPaths(listed.ToArray()));

                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                    var candidate = Path.Combine(drive.Name, "pagefile.sys");
                    ulong size;
                    if (!TryFileSize(candidate, out size)) continue;

                    var known = false;
                    foreach (var path in listed)
                    {
                        if (string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase)) known = true;
                    }

                    if (known) continue;

                    result.Warn(string.Format(CultureInfo.InvariantCulture,
                        "Orphan page file on {0} is not used by Windows", drive.Name),
                        size > 0
                            ? string.Format(CultureInfo.InvariantCulture, "{0} - stale file left over from an earlier configuration",
                                Format.Bytes(size))
                            : "stale file left over from an earlier configuration (size needs admin rights to read)",
                        "disk.pagefile-orphan", "page file files found on volumes that are not configured in Win32_PageFileSetting",
                        "open System Properties > Advanced > Performance Settings > Advanced > Virtual memory, turn on automatic management or remove the stale pagefile.sys");
                }
            }

            if (!context.IsElevated)
            {
                result.Note("Page file size on disk requires administrator rights to read.");
            }
            else
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                    ulong size;
                    if (!TryFileSize(Path.Combine(drive.Name, "pagefile.sys"), out size) || size == 0) continue;
                    result.Note(string.Format(CultureInfo.InvariantCulture,
                        "On disk: {0}pagefile.sys = {1}", drive.Name, Format.Bytes(size)));
                }
            }
        }

        private static void DescribeQueueLength(CheckResult result)
        {
            List<Dictionary<string, object>> disks;

            try
            {
                disks = Wmi.Query("root\\cimv2",
                    "SELECT Name, PercentDiskTime, CurrentDiskQueueLength, AvgDiskQueueLength, DiskBytesPersec " +
                    "FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
            }
            catch (Exception ex)
            {
                result.Note("Disk performance counters unavailable: " + ex.Message);
                return;
            }

            foreach (var disk in disks)
            {
                var name = Wmi.GetString(disk, "Name");
                if (name == "_Total" || name.Length == 0) continue;

                var busy = Wmi.GetNumber(disk, "PercentDiskTime");
                var queue = Wmi.GetNumber(disk, "CurrentDiskQueueLength");
                var throughput = Wmi.GetNumber(disk, "DiskBytesPersec");

                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "Disk activity [{0}]: {1}% busy, queue {2}, {3}/s",
                    name, busy, queue, Format.Bytes(throughput)));

                if (busy >= 90)
                {
                    result.Warn(string.Format(CultureInfo.InvariantCulture,
                        "Disk {0} was {1}% busy at sampling time", name, busy),
                        "a saturated disk stalls the games and recorders reading from it",
                        "disk.busy", "Win32_PerfFormattedData_PerfDisk_PhysicalDisk % Idle Time",
                        "move downloads, captures and installs to another drive while playing - a busy system disk causes stutter");
                }

                if (queue >= 4)
                {
                    result.Warn(string.Format(CultureInfo.InvariantCulture,
                        "Disk {0} queue length is {1}", name, queue),
                        "I/O requests are stacking up",
                        "disk.queue", "Win32_PerfFormattedData_PerfDisk_PhysicalDisk Current Disk Queue Length",
                        "a queue of 4+ at idle means the disk cannot keep up - check for background scanners, indexers or a dying drive");
                }
            }
        }

        private static bool TryFileSize(string path, out ulong size)
        {
            size = 0;

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                size = (ulong)info.Length;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string CleanPath(string path)
        {
            var cleaned = (path ?? string.Empty).Trim();
            if (cleaned.StartsWith("\\??\\", StringComparison.Ordinal)) cleaned = cleaned.Substring(4);
            return cleaned;
        }

        private static string JoinPaths(string[] paths)
        {
            var cleaned = new List<string>();
            foreach (var path in paths)
            {
                var trimmed = (path ?? string.Empty).Trim();
                if (trimmed.Length > 0) cleaned.Add(trimmed);
            }

            return cleaned.Count > 0 ? string.Join("; ", cleaned.ToArray()) : "(none)";
        }
    }
}
