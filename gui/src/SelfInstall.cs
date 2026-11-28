using System;
using System.Diagnostics;
using System.IO;

namespace WarpCord
{
    /// <summary>
    /// "Install for this machine": copy the exe into the user's programs folder and
    /// add Start menu (and optional startup) shortcuts. No admin rights needed.
    /// </summary>
    internal static class SelfInstall
    {
        public static string TargetDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "WarpCord");
            }
        }

        public static string TargetExe
        {
            get { return Path.Combine(TargetDir, "WarpCord.exe"); }
        }

        public static string CurrentExe
        {
            get
            {
                try { return Process.GetCurrentProcess().MainModule.FileName; }
                catch { return ""; }
            }
        }

        public static bool IsInstalled
        {
            get { return File.Exists(TargetExe); }
        }

        public static bool RunningFromTarget
        {
            get
            {
                return string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(TargetExe),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string StartMenuShortcut
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WarpCord.lnk");
            }
        }

        public static string StartupShortcut
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup), "WarpCord.lnk");
            }
        }

        public static bool Install(bool startWithWindows, out string error)
        {
            error = null;
            try
            {
                string source = CurrentExe;
                if (string.IsNullOrEmpty(source) || !File.Exists(source))
                {
                    error = "Çalışan exe bulunamadı.";
                    return false;
                }

                Directory.CreateDirectory(TargetDir);
                string target = TargetExe;
                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    File.Copy(source, target, true);

                if (!CreateShortcut(StartMenuShortcut, target, out error)) return false;
                if (startWithWindows)
                {
                    if (!CreateShortcut(StartupShortcut, target, out error)) return false;
                }
                else
                {
                    TryDelete(StartupShortcut);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Uninstall(out string error)
        {
            error = null;
            try
            {
                TryDelete(StartMenuShortcut);
                TryDelete(StartupShortcut);

                if (RunningFromTarget)
                {
                    error = L10n.T("Kurulu kopyadan çalışıyorsun: önce başka bir kopyayı çalıştırıp kaldır.");
                    return false;
                }

                if (Directory.Exists(TargetDir)) Directory.Delete(TargetDir, true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool CreateShortcut(string shortcutPath, string target, out string error)
        {
            error = null;
            try
            {
                string directory = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                string script =
                    "$w = New-Object -ComObject WScript.Shell; " +
                    "$s = $w.CreateShortcut('" + shortcutPath + "'); " +
                    "$s.TargetPath = '" + target + "'; " +
                    "$s.WorkingDirectory = '" + Path.GetDirectoryName(target) + "'; " +
                    "$s.IconLocation = '" + target + ",0'; " +
                    "$s.Description = 'WarpCord'; " +
                    "$s.Save()";

                string output = ScriptHost.RunCommand(script, 20000);
                return File.Exists(shortcutPath) || string.IsNullOrEmpty(output);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
