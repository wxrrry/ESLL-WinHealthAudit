using System;
using System.Globalization;

namespace WinHealthAudit.Helpers
{
    public static class Format
    {
        public static string Bytes(ulong value)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = value;
            var unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size = size / 1024;
                unit++;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", size, units[unit]);
        }

        public static string Percent(ulong part, ulong total)
        {
            if (total == 0) return "n/a";
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#}%", part * 100.0 / total);
        }

        public static string TimeAgo(DateTime when, DateTime now)
        {
            var span = now - when;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;

            if (span.TotalDays >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0} days ago", span.TotalDays);
            }

            if (span.TotalHours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0} hours ago", span.TotalHours);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0} minutes ago", span.TotalMinutes);
        }
    }
}
