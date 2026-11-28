using System;

namespace WarpCord
{
    /// <summary>Numeric version comparison shared by the updater and the tests.</summary>
    internal static class VersionUtil
    {
        public static int Compare(string left, string right)
        {
            string a = (left ?? "").Trim().TrimStart('v', 'V');
            string b = (right ?? "").Trim().TrimStart('v', 'V');
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int count = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < count; i++)
            {
                int x = Part(pa, i);
                int y = Part(pb, i);
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        private static int Part(string[] parts, int index)
        {
            if (index >= parts.Length) return 0;
            int value;
            string digits = new string(Array.FindAll(parts[index].ToCharArray(), char.IsDigit));
            return int.TryParse(digits, out value) ? value : 0;
        }
    }
}
