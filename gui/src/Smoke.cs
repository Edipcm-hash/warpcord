using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace WarpCord
{
    /// <summary>
    /// Headless self-check: parses the XAML, loads the assets, resolves the
    /// scripts and reads the tunnel state. Exit code 0 means the GUI itself is
    /// healthy (the tunnel is reported but not required to be up).
    /// </summary>
    internal static class Smoke
    {
        private static int _failures;

        public static int Run()
        {
            ConsoleIo.UseUtf8();

            Console.WriteLine("WarpCord arayüzü · duman testi");
            Console.WriteLine();

            var app = new Application();

            Check("tema XAML", delegate
            {
                app.Resources.MergedDictionaries.Add(Xaml.Load<ResourceDictionary>("WarpCord.Theme.xaml"));
            });
            Check("pencere XAML", delegate { Xaml.Load<Grid>("WarpCord.MainWindow.xaml"); });
            Check("logo (256/64/32)", delegate
            {
                if (Assets.Logo256.PixelWidth != 256) throw new Exception("256 px logo okunamadı");
                if (Assets.Logo64.PixelWidth != 64) throw new Exception("64 px logo okunamadı");
                if (Assets.Logo32.PixelWidth != 32) throw new Exception("32 px logo okunamadı");
            });
            Check("motor (klasör veya gömülü)", delegate
            {
                if (Paths.RepoRoot == null) throw new Exception("app\\install.ps1 yok ve gömülü motor açılamadı");
                if (Paths.IsEmbedded && !Engine.IsComplete(Paths.RepoRoot))
                    throw new Exception("gömülü motor eksik: " + Paths.RepoRoot);
            });
            Check("betikler", delegate
            {
                if (Paths.InstallScript == null) throw new Exception("install.ps1 yok");
                if (Paths.HealScript == null) throw new Exception("warpheal.ps1 yok");
                if (Paths.StatusScript == null) throw new Exception("status.ps1 yok");
            });
            Check("PowerShell", delegate
            {
                if (!File.Exists(Paths.PowerShell)) throw new Exception(Paths.PowerShell);
            });
            Check("ayarlar (yaz/oku)", delegate
            {
                string probe = Path.Combine(Path.GetTempPath(), "warpcord-smoke-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(probe, "{\"mode\":\"dpi\",\"force\":true,\"width\":1000}");
                object node = Json.Node(probe);
                File.Delete(probe);
                if (Json.Str(node, "mode") != "dpi" || !Json.Bool(node, false, "force"))
                    throw new Exception("JSON okunamadı");
            });
            Check("uç doğrulama", delegate
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch("162.159.192.1:2408",
                        @"^(?:(?:\d{1,3}\.){3}\d{1,3}:\d{1,5}|\[[0-9A-Fa-f:]+\]:\d{1,5})$"))
                    throw new Exception("geçerli uç reddedildi");
            });

            Info("kaynak", (Paths.IsEmbedded ? "gömülü motor" : "klasör") + " · " + (Paths.RepoRoot ?? "—"));
            Info("manuel", Paths.ManualInstallScript != null ? "WarpCord.bat var" : "WarpCord.bat yok (tek dosya)");

            Console.WriteLine();
            Snapshot snapshot = null;
            try
            {
                snapshot = Prober.CollectAsync(true, true).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [ -- ] durum okunamadı: " + ex.Message);
            }

            if (snapshot != null)
            {
                Info("kurulum", snapshot.Installed ? "var · sürüm " + snapshot.Version : "yok");
                Info("mod", snapshot.ModeName);                Info("sing-box", snapshot.SingBoxRunning ? "çalışıyor · PID " + snapshot.SingBoxPid : "çalışmıyor");
                Info("TUN", snapshot.TunUp ? "Up" : "yukarı değil");
                Info("DNS", snapshot.DnsText);
                Info("Discord API", snapshot.ApiText);
                Info("dış IP", snapshot.PublicIp ?? "—");
                Info("SmartApp", SmartAppControl.Name(SmartAppControl.Read()));
                Info("sonuç", snapshot.Healthy ? "sağlıklı" : "sağlıklı değil (uygulama için hata değil)");
            }

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "SONUC: arayüz hazır."
                : "SONUC: " + _failures + " kontrol başarısız.");
            return _failures == 0 ? 0 : 1;
        }

        private static void Check(string name, Action action)
        {
            try
            {
                action();
                Console.WriteLine("  [ OK ] " + name);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("  [FAIL] " + name + " -> " + ex.Message);
            }
        }

        private static void Info(string name, string value)
        {
            Console.WriteLine("  [ -- ] " + name.PadRight(12) + " " + value);
        }
    }
}
