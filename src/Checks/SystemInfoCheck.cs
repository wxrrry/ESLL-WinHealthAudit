using System;
using System.Collections.Generic;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class SystemInfoCheck : IHealthCheck
    {
        public string Name { get { return "System info"; } }

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name);

            var os = Wmi.First("root\\cimv2",
                "SELECT Caption, Version, BuildNumber, InstallDate, LastBootUpTime, OSArchitecture, LocalDateTime " +
                "FROM Win32_OperatingSystem");

            var computer = Wmi.First("root\\cimv2",
                "SELECT Name, Domain, Workgroup, PartOfDomain, TotalPhysicalMemory " +
                "FROM Win32_ComputerSystem");

            result.Note("OS: " + Wmi.GetString(os, "Caption") + " (" + Wmi.GetString(os, "Version") +
                        ", build " + Wmi.GetString(os, "BuildNumber") + ", " + Wmi.GetString(os, "OSArchitecture") + ")");

            var installed = Wmi.GetTime(os, "InstallDate");
            if (installed.HasValue)
            {
                result.Note("Installed: " + installed.Value.ToString("yyyy-MM-dd"));
            }

            var booted = Wmi.GetTime(os, "LastBootUpTime");
            if (booted.HasValue)
            {
                var now = Wmi.GetTime(os, "LocalDateTime") ?? DateTime.Now;
                result.Note("Last boot: " + booted.Value.ToString("yyyy-MM-dd HH:mm:ss") +
                            " (uptime " + Format.TimeAgo(booted.Value, now) + ")");
            }

            var domain = Wmi.GetString(computer, "Domain");
            result.Note("Machine: " + Wmi.GetString(computer, "Name") + ", domain/workgroup: " + domain);

            var memory = Wmi.GetNumber(computer, "TotalPhysicalMemory");
            if (memory > 0) result.Note("Physical memory: " + Format.Bytes(memory));

            if (!context.IsElevated)
            {
                result.Note("Not elevated: SMART details, some security logs and power requests are skipped.");
            }

            return result;
        }
    }
}
