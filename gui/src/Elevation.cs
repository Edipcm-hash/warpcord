using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ComponentModel;

namespace WarpCord
{
    /// <summary>Admin detection and the "restart with UAC" flow.</summary>
    internal static class Elevation
    {
        private static bool? _isAdmin;

        public static bool IsAdmin
        {
            get
            {
                if (_isAdmin.HasValue) return _isAdmin.Value;
                bool admin = false;
                try
                {
                    using (var identity = WindowsIdentity.GetCurrent())
                    {
                        var principal = new WindowsPrincipal(identity);
                        admin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch
                {
                    admin = false;
                }
                _isAdmin = admin;
                return admin;
            }
        }

        public static string ExecutablePath
        {
            get
            {
                try { return Process.GetCurrentProcess().MainModule.FileName; }
                catch { return System.Reflection.Assembly.GetEntryAssembly().Location; }
            }
        }

        public static bool Relaunch(string arguments, out string error)
        {
            error = null;
            try
            {
                var info = new ProcessStartInfo(ExecutablePath, arguments);
                info.Verb = "runas";
                info.UseShellExecute = true;
                info.WorkingDirectory = Path.GetDirectoryName(ExecutablePath);
                Process.Start(info);
                return true;
            }
            catch (Win32Exception ex)
            {
                error = ex.NativeErrorCode == 1223
                    ? "Yönetici izni verilmedi (UAC iptal edildi)."
                    : "Yönetici olarak başlatılamadı: " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = "Yönetici olarak başlatılamadı: " + ex.Message;
                return false;
            }
        }
    }
}
