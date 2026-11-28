using System;
using System.IO;
using System.Text;
using System.Threading;

namespace WarpCord
{
    /// <summary>
    /// Runs one action without the window and streams its output to the console.
    /// This is the path the GUI uses for the same scripts, so it doubles as an
    /// end-to-end check of the PowerShell plumbing.
    /// </summary>
    internal static class Headless
    {
        public static int RunAction(string name)
        {
            ConsoleIo.UseUtf8();

            if (string.Equals(name, "manual", StringComparison.OrdinalIgnoreCase))
                return LaunchManualInstaller();

            ActionKind kind = Actions.Parse(name, ActionKind.Status);
            var settings = Settings.Load();
            ActionSpec spec = Actions.Build(kind, settings, settings.Endpoint);

            Console.WriteLine("WarpCord · " + spec.Title + " (başsız)");
            if (spec.Script == null)
            {
                Console.Error.WriteLine("Betik bulunamadı: " + spec.MissingRelative +
                                        " (klasör: " + (Paths.RepoRoot ?? "?") + ")");
                return 2;
            }
            if (Actions.NeedsAdmin(kind) && !Elevation.IsAdmin)
            {
                Console.Error.WriteLine("Yönetici hakları gerekiyor.");
                return 3;
            }

            Console.WriteLine("$ " + System.IO.Path.GetFileName(spec.Script) + " " + spec.Switches);
            Console.WriteLine();

            var done = new ManualResetEventSlim(false);
            int exitCode = -1;
            var runner = new Runner();
            runner.Line += delegate(LogLine line) { Console.WriteLine(Decorate(line)); };
            runner.Finished += delegate(int code, TimeSpan elapsed)
            {
                exitCode = code;
                done.Set();
            };

            if (!runner.Start(spec.Script, spec.Switches, Paths.RepoRoot ?? Paths.InstallDir))
            {
                Console.Error.WriteLine("Başlatılamadı.");
                return 4;
            }

            if (!done.Wait(TimeSpan.FromMinutes(15)))
            {
                Console.Error.WriteLine("Zaman aşımı.");
                runner.Cancel();
                return 5;
            }

            Console.WriteLine();
            Console.WriteLine("çıkış kodu: " + exitCode);
            return exitCode;
        }

        /// <summary>The classic route: hand the install to WarpCord.bat in its own console.</summary>
        private static int LaunchManualInstaller()
        {
            string bat = Paths.ManualInstallScript;
            if (bat == null)
            {
                Console.Error.WriteLine("WarpCord.bat bulunamadı (klasör: " + (Paths.RepoRoot ?? "?") + ")");
                return 2;
            }

            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(bat);
                info.Verb = "runas";
                info.UseShellExecute = true;
                info.WorkingDirectory = Paths.RepoRoot;
                System.Diagnostics.Process.Start(info);
                Console.WriteLine("WarpCord.bat ayrı pencerede başlatıldı: " + bat);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Başlatılamadı: " + ex.Message);
                return 4;
            }
        }

        private static string Decorate(LogLine line)
        {
            switch (line.Kind)
            {
                case LogKind.Ok: return "  [ok]   " + line.Text;
                case LogKind.Error: return "  [hata] " + line.Text;
                case LogKind.Warn: return "  [uyar] " + line.Text;
                case LogKind.Step: return "  ==>    " + line.Text;
                default: return "         " + line.Text;
            }
        }
    }
}
