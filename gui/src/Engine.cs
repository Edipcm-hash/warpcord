using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace WarpCord
{
    /// <summary>
    /// The PowerShell engine (installer, status, repair, diagnostics) is embedded
    /// in the exe, so a lone WarpCord.exe on a Desktop still works. On first use
    /// the files are written to %LOCALAPPDATA%\WarpCord\engine\&lt;version&gt;\ and
    /// run from there; a real repo next to the exe always wins over this copy.
    /// </summary>
    internal static class Engine
    {
        private const string Marker = ".engine";

        private static readonly string[] Files =
        {
            @"app\install.ps1",
            @"app\status.ps1",
            @"app\uninstall.ps1",
            @"tools\warpcore.ps1",
            @"tools\warpheal.ps1",
            @"tools\tani.ps1",
            @"tools\warpfrag.ps1",
            @"tools\warpfrag\WarpFrag.cs",
            @"tools\warpreadiness.ps1",
            @"tools\warpendpoints.ps1"
        };

        private static string _root;
        private static bool _failed;

        /// <summary>Extracts (or reuses) the embedded engine; null if it cannot.</summary>
        public static string TryEnsure()
        {
            if (_root != null) return _root;
            if (_failed) return null;

            try
            {
                string parent = Paths.IsPortable
                    ? System.IO.Path.Combine(Paths.AppDir, "engine")
                    : System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WarpCord", "engine");
                string root = System.IO.Path.Combine(parent, AppInfo.Version);

                if (IsComplete(root))
                {
                    _root = root;
                    return root;
                }

                Directory.CreateDirectory(root);
                foreach (string relative in Files) Extract(root, relative);
                File.WriteAllText(System.IO.Path.Combine(root, Marker), AppInfo.Version);

                PruneOld(parent, root);
                _root = root;
                return root;
            }
            catch
            {
                _failed = true;
                return null;
            }
        }

        /// <summary>Manifest resource names the build must embed for the engine.</summary>
        public static IEnumerable<string> ExpectedResources()
        {
            foreach (string relative in Files)
                yield return "WarpCord.Engine." + relative.Replace('\\', '.');
        }
        public static bool IsComplete(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;
            string marker = System.IO.Path.Combine(root, Marker);
            if (!File.Exists(marker)) return false;
            try
            {
                if (File.ReadAllText(marker).Trim() != AppInfo.Version) return false;
            }
            catch
            {
                return false;
            }
            foreach (string relative in Files)
            {
                if (!File.Exists(System.IO.Path.Combine(root, relative))) return false;
            }
            return true;
        }

        private static void Extract(string root, string relative)
        {
            Assembly assembly = typeof(Engine).Assembly;
            string resource = "WarpCord.Engine." + relative.Replace('\\', '.');

            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                    throw new InvalidOperationException("Gömülü motor dosyası bulunamadı: " + resource);

                string destination = System.IO.Path.Combine(root, relative);
                string directory = System.IO.Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (FileStream file = File.Create(destination)) stream.CopyTo(file);
            }
        }

        /// <summary>Drops engine copies of older GUI versions (best effort).</summary>
        private static void PruneOld(string parent, string keep)
        {
            try
            {
                foreach (string directory in Directory.GetDirectories(parent))
                {
                    if (string.Equals(directory, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(directory, true); }
                    catch { }
                }
            }
            catch
            {
                // leftovers are harmless
            }
        }
    }
}
