using System.Collections.Generic;
using WinHealthAudit.Checks;

namespace WinHealthAudit
{
    /// <summary>
    /// The checks the audit runs, in report order. Used by the console and the GUI.
    /// </summary>
    public static class HealthChecks
    {
        public static IEnumerable<IHealthCheck> All()
        {
            return new IHealthCheck[]
            {
                new SystemInfoCheck(),
                new HardwareCheck(),
                new DiskCheck(),
                new PerformanceCheck(),
                new WheaErrorsCheck(),
                new EventLogCheck(),
                new DeviceManagerCheck(),
                new ProcessesCheck(),
                new SystemStateCheck()
            };
        }
    }
}
