using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;

namespace WinHealthAudit.Helpers
{
    public static class Wmi
    {
        public static List<Dictionary<string, object>> Query(string scope, string wql)
        {
            var rows = new List<Dictionary<string, object>>();

            using (var searcher = new ManagementObjectSearcher(scope, wql))
            using (var results = searcher.Get())
            {
                foreach (ManagementBaseObject item in results)
                {
                    rows.Add(Copy(item));
                }
            }

            return rows;
        }

        public static Dictionary<string, object> First(string scope, string wql)
        {
            var rows = Query(scope, wql);
            return rows.Count > 0 ? rows[0] : null;
        }

        public static string GetString(IDictionary<string, object> row, string property)
        {
            object value;
            if (row == null || !row.TryGetValue(property, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        public static ulong GetNumber(IDictionary<string, object> row, string property)
        {
            object value;
            if (row == null || !row.TryGetValue(property, out value) || value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static DateTime? GetTime(IDictionary<string, object> row, string property)
        {
            var raw = GetString(row, property);
            if (raw.Length == 0) return null;

            try
            {
                return ManagementDateTimeConverter.ToDateTime(raw);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool GetBool(IDictionary<string, object> row, string property)
        {
            object value;
            if (row == null || !row.TryGetValue(property, out value) || value == null)
            {
                return false;
            }

            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Dictionary<string, object> Copy(ManagementBaseObject source)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (PropertyData property in source.Properties)
            {
                row[property.Name] = property.Value;
            }

            return row;
        }
    }
}
