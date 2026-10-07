using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class GamingCheck : IHealthCheck
    {
        public string Name { get { return "Gaming profile"; } }

        private const string PowerSaverScheme = "a1841308-3541-4fab-bc81-f71556f20b4a";

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name,
                "Display refresh rate, Game Mode, capture, GPU scheduling and power plan");

            DescribeRefresh(result);
            DescribeGameMode(result);
            DescribeCapture(result);
            DescribeHags(result);
            DescribePower(result);

            return result;
        }

        private static void DescribeRefresh(CheckResult result)
        {
            List<Dictionary<string, object>> rows;

            try
            {
                rows = Wmi.Query("root\\cimv2",
                    "SELECT CurrentRefreshRate, MaxRefreshRate FROM Win32_VideoController");
            }
            catch (Exception ex)
            {
                result.Note("Display refresh rate unavailable: " + ex.Message);
                return;
            }

            var current = 0UL;
            var max = 0UL;
            var bestCurrent = 0UL;

            foreach (var row in rows)
            {
                var cur = Wmi.GetNumber(row, "CurrentRefreshRate");
                var cap = Wmi.GetNumber(row, "MaxRefreshRate");

                if (cur > current) current = cur;
                if (cap > max)
                {
                    max = cap;
                    bestCurrent = cur;
                }
            }

            if (max == 0 && current == 0)
            {
                result.Note("Display refresh rate not reported by WMI.");
                return;
            }

            var effectiveCurrent = bestCurrent > 0 ? bestCurrent : current;
            if (max == 0) max = effectiveCurrent;

            if (effectiveCurrent < max)
            {
                result.Warn(string.Format(CultureInfo.InvariantCulture,
                    "Display runs at {0} Hz while {1} Hz is available", effectiveCurrent, max),
                    "the adapter reports " + effectiveCurrent + " Hz active against a " + max + " Hz capability",
                    "gaming.refresh-avail", "Win32_VideoController CurrentRefreshRate vs MaxRefreshRate",
                    "Settings > System > Display > Advanced display - pick the highest refresh rate your monitor supports");
            }
            else if (effectiveCurrent <= 60)
            {
                result.Warn(string.Format(CultureInfo.InvariantCulture,
                    "Display tops out at {0} Hz", effectiveCurrent),
                    "the highest refresh rate Windows reports is " + effectiveCurrent + " Hz",
                    "gaming.refresh-low", "Win32_VideoController MaxRefreshRate",
                    "check the display driver and cable - at 60 Hz the game feels slower even on a fast GPU");
            }
            else
            {
                result.Note(string.Format(CultureInfo.InvariantCulture,
                    "Display: {0} Hz refresh rate in use.", effectiveCurrent));
            }
        }

        private static void DescribeGameMode(CheckResult result)
        {
            var allow = ReadDword(Registry.CurrentUser, "Software\\Microsoft\\GameBar", "AllowAutoGameMode");
            if (allow == null)
            {
                allow = ReadDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\GameBar", "AllowAutoGameMode");
            }

            if (allow == 0)
            {
                result.Warn("Windows Game Mode is off", "AllowAutoGameMode = 0",
                    "gaming.gamemode-off", "GameBar registry AllowAutoGameMode",
                    "Settings > Gaming > Game Mode - turn it on so Windows prioritises the game");
            }
            else if (allow == 1)
            {
                result.Note("Game Mode: on.");
            }
            else
            {
                result.Note("Game Mode: on (Windows default - the key is not set).");
            }
        }

        private static void DescribeCapture(CheckResult result)
        {
            var policy = ReadDword(Registry.LocalMachine,
                "SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR", "AllowGameDVR");
            var allowed = ReadDword(Registry.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled");
            var capture = ReadDword(Registry.CurrentUser,
                "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled");
            var historical = ReadDword(Registry.CurrentUser,
                "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "HistoricalCaptureEnabled");
            var autoRecord = ReadDword(Registry.CurrentUser, "Software\\Microsoft\\GameBar", "AutoRecordEnabled");

            if (policy == 0 || allowed == 0)
            {
                result.Note("Game Bar capture: disabled.");
                return;
            }

            if (capture == 1 || historical == 1 || autoRecord == 1)
            {
                result.Warn("Game Bar background capture is on",
                    "a capture value is set to 1 while you play",
                    "gaming.capture-on", "GameDVR / GameBar capture registry values",
                    "Settings > Gaming > Captures - turn off background recording while you play");
            }
            else
            {
                result.Note("Game Bar capture: allowed, background recording is not enabled.");
            }
        }

        private static void DescribeHags(CheckResult result)
        {
            var hags = ReadDword(Registry.LocalMachine,
                "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode");

            if (hags == null)
            {
                result.Note("HAGS (hardware GPU scheduling): Windows default (not configured).");
            }
            else if (hags == 2)
            {
                result.Note("HAGS (hardware GPU scheduling): on (HwSchMode = 2).");
            }
            else if (hags == 1)
            {
                result.Note("HAGS (hardware GPU scheduling): off (HwSchMode = 1).");
            }
            else
            {
                result.Note("HAGS (hardware GPU scheduling): HwSchMode = " + hags + ".");
            }
        }

        private static void DescribePower(CheckResult result)
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
                result.Note("Power plan unavailable: " + ex.Message);
                return;
            }

            if (string.Equals(scheme, PowerSaverScheme, StringComparison.OrdinalIgnoreCase))
            {
                result.Warn("Power plan is Power saver",
                    "the scheme keeps clocks low while you play",
                    "gaming.power-saver", "Power\\User\\PowerSchemes ActivePowerScheme = " + scheme,
                    "Settings > System > Power & battery - pick Balanced or Best performance while gaming");
            }
        }

        private static int? ReadDword(RegistryKey root, string path, string name)
        {
            try
            {
                using (var key = root.OpenSubKey(path))
                {
                    if (key == null) return null;
                    var value = key.GetValue(name);
                    if (value == null) return null;
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
