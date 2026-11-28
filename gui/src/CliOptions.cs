using System;
using System.Collections.Generic;
using System.Globalization;

namespace WarpCord
{
    /// <summary>Command line of the GUI itself.</summary>
    internal sealed class CliOptions
    {
        public string Render;
        public double RenderScale = 2;
        public double RenderWidth;
        public double RenderHeight;
        public bool Smoke;
        public string RepoRoot;
        public bool Demo;
        public string RunAction;
        public string PreviewState;
        public string ExecAction;
        public bool AdvancedPreview;
        public string PromptRender;
        public bool UseEmbeddedEngine;
        public bool Help;

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch ((arg ?? "").ToLowerInvariant())
                {
                    case "--render":
                        options.Render = Next(args, ref i, "--render");
                        break;
                    case "--scale":
                        options.RenderScale = ToDouble(Next(args, ref i, "--scale"), 2);
                        break;
                    case "--size":
                        {
                            string size = Next(args, ref i, "--size");
                            string[] parts = (size ?? "").Split('x', 'X');
                            if (parts.Length == 2)
                            {
                                options.RenderWidth = ToDouble(parts[0], 0);
                                options.RenderHeight = ToDouble(parts[1], 0);
                            }
                            else throw new ArgumentException("--size WxH biçiminde olmalı, örn: 1000x760");
                        }
                        break;
                    case "--smoke":
                        options.Smoke = true;
                        break;
                    case "--repo":
                        options.RepoRoot = Next(args, ref i, "--repo");
                        break;
                    case "--demo":
                        options.Demo = true;
                        break;
                    case "--run":
                        options.RunAction = Next(args, ref i, "--run");
                        break;
                    case "--state":
                        options.PreviewState = Next(args, ref i, "--state");
                        break;
                    case "--exec":
                        options.ExecAction = Next(args, ref i, "--exec");
                        break;
                    case "--advanced":
                        options.AdvancedPreview = true;
                        break;
                    case "--prompt":
                        options.PromptRender = Next(args, ref i, "--prompt");
                        break;
                    case "--engine":
                        options.UseEmbeddedEngine = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.Help = true;
                        break;
                }
            }
            return options;
        }

        private static string Next(string[] args, ref int index, string name)
        {
            if (index + 1 >= args.Length) throw new ArgumentException(name + " bir değer bekliyor");
            index++;
            return args[index];
        }

        private static double ToDouble(string text, double fallback)
        {
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return value;
            return fallback;
        }

        public static string Usage()
        {
            return "WarpCord arayüzü\n" +
                   "  WarpCord.exe                 arayüzü aç\n" +
                   "  WarpCord.exe --smoke         kendi kendini test et (konsol)\n" +
                   "  WarpCord.exe --exec status   bir işlemi arayüzsüz çalıştır (status|heal|install|diagnose|uninstall|manual)\n" +
                   "  WarpCord.exe --render p.png  arayüzü PNG olarak çiz (belge/görsel test)\n" +
                   "      --size 1000x760 --scale 2 --demo --state broken|notinstalled|dpi --repo <klasör>\n" +
                   "  WarpCord.exe --prompt p.png  kurulum yöntemi penceresini PNG olarak çiz\n" +
                   "      --engine                 (varsa) klasördeki betikleri değil, exe'ye gömülü motoru kullan\n" +
                   "  WarpCord.exe --run install   kurulumu doğrudan başlat (yükseltilmiş kopya için)\n";
        }
    }

    /// <summary>UTF-8 console output for the headless modes (help, smoke, exec).</summary>
    internal static class ConsoleIo
    {
        private static bool _done;

        public static void UseUtf8()
        {
            if (_done) return;
            _done = true;
            try
            {
                var stdout = new System.IO.StreamWriter(
                    Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
                stdout.AutoFlush = true;
                Console.SetOut(stdout);

                var stderr = new System.IO.StreamWriter(
                    Console.OpenStandardError(), new System.Text.UTF8Encoding(false));
                stderr.AutoFlush = true;
                Console.SetError(stderr);
            }
            catch
            {
                // encoding is cosmetic; keep the default writer
            }
        }
    }

    /// <summary>Single instance guard, released when the app re-launches itself elevated.</summary>
    internal static class SingleInstance
    {
        private const string MutexName = @"Local\WarpCord.Gui.1";
        private static System.Threading.Mutex _mutex;
        private static bool _owned;

        public static bool Acquire()
        {
            try
            {
                bool created;
                _mutex = new System.Threading.Mutex(true, MutexName, out created);
                _owned = created;
                return created;
            }
            catch
            {
                return true;
            }
        }

        public static void Release()
        {
            if (_mutex == null) return;
            try
            {
                if (_owned) _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
            catch
            {
                // already gone
            }
            _mutex = null;
            _owned = false;
        }
    }

    internal static class CrashLog
    {
        public static void Write(Exception ex)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Paths.AppDataDir);
                System.IO.File.AppendAllText(Paths.CrashLogPath,
                    "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\r\n" + ex + "\r\n\r\n");
            }
            catch
            {
                // nothing left to do
            }
        }
    }
}
