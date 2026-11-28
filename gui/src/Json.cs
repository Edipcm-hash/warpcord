using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WarpCord
{
    /// <summary>Small, tolerant JSON helper around <see cref="JavaScriptSerializer"/>.</summary>
    internal static class Json
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static string ReadAllText(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        public static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf('{');
            if (start < 0) return null;
            try { return Serializer.DeserializeObject(text); }
            catch { return null; }
        }

        public static object Node(string path)
        {
            return Parse(ReadAllText(path));
        }

        public static object Get(object node, params string[] keys)
        {
            object current = node;
            foreach (string key in keys)
            {
                if (current == null) return null;
                var map = current as IDictionary<string, object>;
                if (map == null) return null;
                object next;
                if (!map.TryGetValue(key, out next)) return null;
                current = next;
            }
            return current;
        }

        public static string Str(object node, params string[] keys)
        {
            object value = Get(node, keys);
            if (value == null) return null;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static int Int(object node, int fallback, params string[] keys)
        {
            object value = Get(node, keys);
            if (value == null) return fallback;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        public static bool Bool(object node, bool fallback, params string[] keys)
        {
            object value = Get(node, keys);
            if (value == null) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed)) return parsed;
            return fallback;
        }

        public static string Serialize(IDictionary<string, object> map)
        {
            return Serializer.Serialize(map);
        }
    }
}
