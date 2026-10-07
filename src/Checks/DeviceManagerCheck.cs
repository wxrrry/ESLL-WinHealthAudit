using System;
using System.Collections.Generic;
using System.Globalization;
using WinHealthAudit.Helpers;

namespace WinHealthAudit.Checks
{
    public sealed class DeviceManagerCheck : IHealthCheck
    {
        public string Name { get { return "Device manager"; } }

        private static readonly Dictionary<int, string> Codes = BuildCodes();

        public CheckResult Run(AuditContext context)
        {
            var result = new CheckResult(Name);

            List<Dictionary<string, object>> devices;

            try
            {
                devices = Wmi.Query("root\\cimv2",
                    "SELECT Name, Status, ConfigManagerErrorCode, PNPDeviceID FROM Win32_PnPEntity " +
                    "WHERE ConfigManagerErrorCode <> 0");
            }
            catch (Exception ex)
            {
                result.Note("Device query unavailable: " + ex.Message);
                return result;
            }

            if (devices.Count == 0)
            {
                result.Note("No devices report an error code.");
                return result;
            }

            result.Note(devices.Count + " device(s) report a problem code.");

            foreach (var device in devices)
            {
                var code = (int)Wmi.GetNumber(device, "ConfigManagerErrorCode");
                string description;
                if (!Codes.TryGetValue(code, out description)) description = "problem code " + code;

                var name = Wmi.GetString(device, "Name");
                if (name.Length == 0) name = Wmi.GetString(device, "PNPDeviceID");

                var message = string.Format(CultureInfo.InvariantCulture,
                    "{0} - {1} (code {2})",
                    name, description, code);

                result.Warn(message, Wmi.GetString(device, "PNPDeviceID"));
            }

            return result;
        }

        private static Dictionary<int, string> BuildCodes()
        {
            var codes = new Dictionary<int, string>();
            codes[1] = "device not configured correctly";
            codes[3] = "driver for the device is corrupted";
            codes[10] = "device cannot start";
            codes[12] = "cannot find free resources for the device";
            codes[14] = "device needs to be reinstalled";
            codes[16] = "cannot locate the resources the device requires";
            codes[19] = "drivers must be reinstalled";
            codes[22] = "device is disabled";
            codes[28] = "drivers are not installed";
            codes[31] = "device is not working properly";
            codes[32] = "driver is not applicable for this device";
            codes[37] = "device cannot allocate the resources it needs";
            codes[38] = "driver failed to initialize the device";
            codes[39] = "registry description is damaged";
            codes[43] = "device was stopped and restarted after an error";
            codes[47] = "device is waiting to be removed";
            codes[48] = "device cannot start because its drivers are blocked";
            codes[52] = "Windows cannot verify the digital signature of the driver";
            codes[54] = "device was disabled by the user";
            codes[56] = "device firmware handoff is still pending";
            return codes;
        }
    }
}
