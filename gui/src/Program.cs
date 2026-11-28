using System;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace WarpCord
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Discord refuses anything below TLS 1.2; the framework default can still be 1.0.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;
            }
            catch
            {
                // older framework: the platform default stands
            }

            CliOptions cli;
            ConsoleIo.UseUtf8();
            try
            {
                cli = CliOptions.Parse(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(CliOptions.Usage());
                return 64;
            }

            if (cli.Help)
            {
                Console.WriteLine(CliOptions.Usage());
                return 0;
            }
            if (cli.UseEmbeddedEngine) Paths.ForceEmbedded();
            if (cli.RepoRoot != null) Paths.OverrideRepo(cli.RepoRoot);
            if (cli.SelfTest) return SelfTest.Run();
            if (cli.Smoke) return Smoke.Run();
            if (!string.IsNullOrEmpty(cli.ExecAction)) return Headless.RunAction(cli.ExecAction);

            bool guard = cli.Render == null && string.IsNullOrEmpty(cli.RunAction);
            if (guard && !SingleInstance.Acquire())
            {
                MessageBox.Show("WarpCord zaten açık.", "WarpCord",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                CrashLog.Write(e.Exception);
                MessageBox.Show("Beklenmeyen bir hata oluştu:\n\n" + e.Exception.Message +
                                "\n\nAyrıntılar: " + Paths.CrashLogPath,
                    "WarpCord", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null) CrashLog.Write(ex);
            };

            try
            {
                app.Resources.MergedDictionaries.Add(Xaml.Load<ResourceDictionary>("WarpCord.Theme.xaml"));
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                MessageBox.Show("Tema yüklenemedi:\n\n" + ex.Message, "WarpCord",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return 3;
            }

            var settings = Settings.Load();

            if (!string.IsNullOrEmpty(cli.PromptRender))
            {
                try
                {
                    InstallPrompt.RenderTo(cli.PromptRender, 2);
                    return 0;
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                    Console.Error.WriteLine(ex.Message);
                    return 5;
                }
            }

            if (!string.IsNullOrEmpty(cli.SettingsRender))
            {
                try
                {
                    SettingsWindow.RenderTo(cli.SettingsRender, 1);
                    return 0;
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                    Console.Error.WriteLine(ex.Message);
                    return 5;
                }
            }

            MainWindow window;
            try
            {
                window = new MainWindow(settings, cli);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                MessageBox.Show("Arayüz yüklenemedi:\n\n" + ex.Message, "WarpCord",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return 4;
            }

            app.Run(window);
            return 0;
        }
    }
}
