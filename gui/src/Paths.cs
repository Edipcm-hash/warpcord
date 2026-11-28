using System;
using System.IO;

namespace WarpCord
{
    /// <summary>Everything the GUI needs to know about where WarpCord lives.</summary>
    internal static class Paths
    {
        private const string InstallDirPath = @"C:\ProgramData\DiscordTunnel";

        private static string _repo;
        private static string _source;
        private static bool _forceEmbedded;

        public static string InstallDir
        {
            get { return InstallDirPath; }
        }

        public static string RepoRoot
        {
            get
            {
                if (_repo == null) _repo = DetectRepo();
                return _repo;
            }
        }

        /// <summary>"repo" (next to the exe), "repo" (recorded by the installer) or "engine".</summary>
        public static string Source
        {
            get
            {
                string ignored = RepoRoot;
                if (ignored == null) return null;
                return _source;
            }
        }

        public static bool IsEmbedded
        {
            get { return Source == "engine"; }
        }

        public static void OverrideRepo(string path)
        {
            if (!string.IsNullOrEmpty(path) && IsRepo(path))
            {
                _repo = path;
                _source = "repo";
            }
        }

        /// <summary>Ignore any real repo and use the engine embedded in the exe (tests, troubleshooting).</summary>
        public static void ForceEmbedded()
        {
            _forceEmbedded = true;
            _repo = null;
            _source = null;
        }

        private static string DetectRepo()
        {
            if (!_forceEmbedded)
            {
                // 1. walk up from the executable: dist\WarpCord.exe -> repo root
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
                for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
                {
                    if (IsRepo(dir.FullName))
                    {
                        _source = "repo";
                        return dir.FullName;
                    }
                }

                // 2. the installer records the folder it was installed from
                string recorded = Json.Str(Json.Node(StatePath), "repoPath");
                if (IsRepo(recorded))
                {
                    _source = "repo";
                    return recorded;
                }
            }

            // 3. the engine embedded in this exe: a lone WarpCord.exe still works
            string engine = Engine.TryEnsure();
            if (engine != null)
            {
                _source = "engine";
                return engine;
            }

            _source = null;
            return null;
        }

        public static bool IsRepo(string dir)
        {
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, @"app\install.ps1"));
        }

        /// <summary>Repo-relative script, falling back to the copy inside the install dir.</summary>
        public static string Script(string repoRelative, string installRelative)
        {
            string repo = RepoRoot;
            if (repo != null)
            {
                string candidate = Path.Combine(repo, repoRelative);
                if (File.Exists(candidate)) return candidate;
            }
            if (installRelative != null)
            {
                string candidate = Path.Combine(InstallDir, installRelative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        public static string InstallScript { get { return Script(@"app\install.ps1", null); } }
        public static string UninstallScript { get { return Script(@"app\uninstall.ps1", null); } }
        public static string StatusScript { get { return Script(@"app\status.ps1", null); } }
        public static string HealScript { get { return Script(@"tools\warpheal.ps1", @"tools\warpheal.ps1"); } }
        public static string DiagnoseScript { get { return Script(@"tools\tani.ps1", null); } }
        public static string SacScript { get { return Script(@"tools\warpac.ps1", @"tools\warpac.ps1"); } }

        /// <summary>The classic one-click launcher next to this repo (manual install route).</summary>
        public static string ManualInstallScript
        {
            get
            {
                string repo = RepoRoot;
                if (repo == null || IsEmbedded) return null;
                string bat = Path.Combine(repo, "WarpCord.bat");
                return File.Exists(bat) ? bat : null;
            }
        }

        public static string StatePath { get { return Path.Combine(InstallDir, "state.json"); } }
        public static string HealStatePath { get { return Path.Combine(InstallDir, "heal-state.json"); } }
        public static string HealLogPath { get { return Path.Combine(InstallDir, "heal.log"); } }
        public static string TunnelLogPath { get { return Path.Combine(InstallDir, "tunnel.log"); } }
        public static string InstallLogPath { get { return Path.Combine(InstallDir, "install.log"); } }
        public static string ConfigPath { get { return Path.Combine(InstallDir, "config.json"); } }

        public static bool Installed { get { return Directory.Exists(InstallDir); } }

        public static string AppDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WarpCord");
            }
        }

        /// <summary>The folder the exe lives in (used for portable mode and the engine).</summary>
        public static string AppDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); }
        }

        private static bool? _portable;

        /// <summary>Portable: settings and the engine live next to the exe ("portable.txt").</summary>
        public static bool IsPortable
        {
            get
            {
                if (_portable.HasValue) return _portable.Value;
                bool portable = false;
                try { portable = File.Exists(Path.Combine(AppDir, "portable.txt")); }
                catch { }
                _portable = portable;
                return portable;
            }
        }

        public static string SettingsPath
        {
            get
            {
                return IsPortable
                    ? Path.Combine(AppDir, "ui.json")
                    : Path.Combine(AppDataDir, "ui.json");
            }
        }

        public static string CrashLogPath
        {
            get
            {
                return IsPortable
                    ? Path.Combine(AppDir, "crash.log")
                    : Path.Combine(AppDataDir, "crash.log");
            }
        }

        public static string PowerShell
        {
            get
            {
                string exe = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"System32\WindowsPowerShell\v1.0\powershell.exe");
                return File.Exists(exe) ? exe : "powershell.exe";
            }
        }
    }
}
