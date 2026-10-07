using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class SystemStateCheck : IHealthCheck
    {
        public string Name { get { return "System state"; } }

        private static readonly Dictionary<string, string> PowerSchemes = BuildPowerSchemes();

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name);

            DescribeRebootPending(result);
            DescribePowerScheme(result);
            DescribeDefender(result);
            DescribeActivation(result);

            return result;
        }

        private static void DescribeRebootPending(CheckResult result)
        {
            var reasons = new List<string>();

            if (IsKeyPresent(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\RebootPending"))
            {
                reasons.Add("component based servicing");
            }

            if (IsKeyPresent(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Auto Update\\RebootRequired"))
            {
                reasons.Add("Windows Update");
            }

            if (reasons.Count > 0)
            {
                result.Warn("A reboot is pending", string.Join(", ", reasons.ToArray()));
            }

            var renames = PendingFileRenames();

            if (renames > 0)
            {
                result.Note(renames + " file rename operation(s) wait for a reboot " +
                            "(normally leftovers from an installer, harmless on their own).");
            }

            if (reasons.Count == 0 && renames == 0)
            {
                result.Note("No reboot is pending.");
            }
        }

        private static int PendingFileRenames()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    "SYSTEM\\CurrentControlSet\\Control\\Session Manager"))
                {
                    var renames = key == null ? null : key.GetValue("PendingFileRenameOperations") as string[];
                    return renames == null ? 0 : renames.Length;
                }
            }
            catch (Exception)
            {
                // Not readable without elevation; the check stays silent.
            }

            return 0;
        }

        private static void DescribePowerScheme(CheckResult result)
        {
            string scheme;

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes"))
                {
                    scheme = key == null ? string.Empty : Convert.ToString(key.GetValue("ActivePowerScheme") ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                result.Note("Power scheme unavailable: " + ex.Message);
                return;
            }

            string friendly;
            if (!PowerSchemes.TryGetValue(scheme, out friendly)) friendly = scheme;

            result.Note("Power scheme: " + friendly + " (" + scheme + ")");

            var aspm = ReadAspmSetting(scheme);
            if (aspm.HasValue)
            {
                result.Note("PCI link power management (ASPM) index: " + aspm.Value +
                            " (0 = off, 1 = moderate, 2 = aggressive) - value only, not a verdict.");
            }
        }

        private static byte? ReadAspmSetting(string scheme)
        {
            if (scheme.Length == 0) return null;

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes\\" + scheme +
                    "\\501a4d13-42af-4429-9fd1-a8218c268e20\\ee12f906-d277-404b-b6da-e5fa1a576df5"))
                {
                    var value = key == null ? null : key.GetValue("ACSettingIndex");
                    if (value is byte) return (byte)value;
                    if (value is int) return (byte)(int)value;
                }
            }
            catch (Exception)
            {
                // Best effort only.
            }

            return null;
        }

        private static void DescribeDefender(CheckResult result)
        {
            Dictionary<string, object> status;

            try
            {
                status = Wmi.First("root\\Microsoft\\Windows\\Defender",
                    "SELECT RealTimeProtectionEnabled, AntivirusEnabled, AMServiceEnabled, " +
                    "AntivirusSignatureAge, QuickScanAge, FullScanAge, IsTamperProtected " +
                    "FROM MSFT_MpComputerStatus");
            }
            catch (Exception ex)
            {
                result.Note("Defender status unavailable: " + ex.Message);
                return;
            }

            if (status == null)
            {
                result.Note("No Defender status returned (third party antivirus may be in control).");
                return;
            }

            var realtime = Wmi.GetBool(status, "RealTimeProtectionEnabled");
            var signatureAge = Wmi.GetNumber(status, "AntivirusSignatureAge");
            var fullScanAge = Wmi.GetNumber(status, "FullScanAge");

            result.Note(string.Format(CultureInfo.InvariantCulture,
                "Defender: real-time protection {0}, signatures {1} day(s) old",
                realtime ? "on" : "OFF", signatureAge));

            if (!realtime)
            {
                result.Fail("Real-time protection is off", "the machine is unprotected until it is restored");
            }

            if (signatureAge > 7)
            {
                result.Warn("Antivirus signatures are older than a week", signatureAge + " days old");
            }

            if (fullScanAge > 3650)
            {
                result.Note("A full scan has never run on this system.");
            }
            else
            {
                result.Note("Last full scan: " + fullScanAge + " day(s) ago.");
            }
        }

        private static void DescribeActivation(CheckResult result)
        {
            List<Dictionary<string, object>> products;

            try
            {
                products = Wmi.Query("root\\cimv2",
                    "SELECT Name, Description, PartialProductKey, LicenseStatus FROM SoftwareLicensingProduct " +
                    "WHERE PartialProductKey IS NOT NULL");
            }
            catch (Exception ex)
            {
                result.Note("Activation status unavailable: " + ex.Message);
                return;
            }

            foreach (var product in products)
            {
                var name = Wmi.GetString(product, "Name");
                if (name.Length == 0) name = Wmi.GetString(product, "Description");

                var status = Wmi.GetNumber(product, "LicenseStatus");
                var key = Wmi.GetString(product, "PartialProductKey");

                if (status == 1)
                {
                    result.Note(name + ": licensed (key ...-" + key + ")");
                }
                else
                {
                    result.Fail(name + ": not activated", "license status code " + status);
                }
            }
        }

        private static Dictionary<string, string> BuildPowerSchemes()
        {
            var schemes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            schemes["381b4222-f694-41f0-9685-ff5bb260df2e"] = "Balanced";
            schemes["8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"] = "High performance";
            schemes["a1841308-3541-4fab-bc81-f71556f20b4a"] = "Power saver";
            schemes["95533000-9d47-4c57-a0f4-33a9c5a0e1a6"] = "Custom scheme";
            schemes["e9a42b02-d5df-448d-aa00-03f14749eb61"] = "Ultimate performance";
            return schemes;
        }

        private static bool IsKeyPresent(string path)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(path))
                {
                    return key != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
