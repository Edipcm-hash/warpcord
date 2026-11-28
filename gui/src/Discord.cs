using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace WarpCord
{
    /// <summary>Find, launch and restart the Discord client (the classic installer).</summary>
    internal static class DiscordClient
    {
        public static string Root
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Discord");
            }
        }

        public static string UpdateExe
        {
            get
            {
                try
                {
                    string path = Path.Combine(Root, "Update.exe");
                    return File.Exists(path) ? path : null;
                }
                catch { return null; }
            }
        }

        public static string DiscordExe
        {
            get
            {
                try
                {
                    if (!Directory.Exists(Root)) return null;
                    return Directory.GetFiles(Root, "Discord.exe", SearchOption.AllDirectories)
                        .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                        .FirstOrDefault();
                }
                catch { return null; }
            }
        }

        public static bool IsInstalled
        {
            get { return UpdateExe != null || DiscordExe != null; }
        }

        public static bool IsRunning
        {
            get
            {
                try { return Process.GetProcessesByName("Discord").Length > 0; }
                catch { return false; }
            }
        }

        public static bool Start(out string error)
        {
            error = null;
            try
            {
                string update = UpdateExe;
                if (update != null)
                {
                    var info = new ProcessStartInfo(update, "--processStart Discord.exe");
                    info.UseShellExecute = true;
                    info.WorkingDirectory = Root;
                    Process.Start(info);
                    return true;
                }

                string exe = DiscordExe;
                if (exe != null)
                {
                    var info = new ProcessStartInfo(exe);
                    info.UseShellExecute = true;
                    info.WorkingDirectory = Path.GetDirectoryName(exe);
                    Process.Start(info);
                    return true;
                }

                error = "Discord kurulu değil ya da yol bulunamadı.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Close every Discord/Update process, wait for them, then start Discord again.</summary>
        public static bool Restart(out string error)
        {
            error = null;
            if (!IsInstalled)
            {
                error = "Discord kurulu değil ya da yol bulunamadı.";
                return false;
            }

            try
            {
                foreach (string name in new[] { "Discord", "Update" })
                {
                    Process[] processes = Process.GetProcessesByName(name);
                    foreach (Process process in processes)
                    {
                        try { process.Kill(); }
                        catch { }
                        finally { process.Dispose(); }
                    }
                }

                for (int i = 0; i < 20; i++)
                {
                    if (!IsRunning) break;
                    Thread.Sleep(150);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return Start(out error);
        }
    }
}
