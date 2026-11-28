using System;
using System.IO;

namespace WarpCord
{
    /// <summary>Everything the GUI needs to know about where WarpCord lives.</summary>
    internal static class Paths
    {
        public const string InstallDir = @"C:\ProgramData\DiscordTunnel";

        private static string _repo;

        public static string RepoRoot
        {
            get
            {
                if (_repo == null) _repo = DetectRepo();
                return _repo;
            }
        }

        public static void OverrideRepo(string path)
        {
            if (!string.IsNullOrEmpty(path) && IsRepo(path)) _repo = path;
        }

        private static string DetectRepo()
        {
            // 1. walk up from the executable: dist\WarpCord.exe -> repo root
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
            for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
            {
                if (IsRepo(dir.FullName)) return dir.FullName;
            }

            // 2. the installer records the folder it was installed from
            string recorded = Json.Str(Json.Node(StatePath), "repoPath");
            if (IsRepo(recorded)) return recorded;

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

        /// <summary>The classic one-click launcher next to this repo (manual install route).</summary>
        public static string ManualInstallScript
        {
            get
            {
                string repo = RepoRoot;
                if (repo == null) return null;
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

        public static string SettingsPath { get { return Path.Combine(AppDataDir, "ui.json"); } }
        public static string CrashLogPath { get { return Path.Combine(AppDataDir, "crash.log"); } }

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
