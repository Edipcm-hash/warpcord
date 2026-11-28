using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarpCord
{
    /// <summary>
    /// Tiny two-language layer. The key is the Turkish source string, so XAML
    /// needs no annotations: <see cref="Apply"/> walks the window once and swaps
    /// any known literal, and code paths call <see cref="T"/> for dynamic text.
    /// </summary>
    internal static class L10n
    {
        public static string Lang = "tr";

        public static bool IsEnglish
        {
            get { return Lang == "en"; }
        }

        private static readonly string[,] Pairs = new string[,]
        {
            // window chrome and hero
            { "Discord'a özel split tunnel", "Discord-only split tunnel" },
            { "Sadece Discord trafiği tünele girer, geri kalan her şey doğrudan gider.", "Only Discord traffic enters the tunnel; everything else goes direct." },
            { "Küçült", "Minimise" },
            { "Büyüt", "Maximise" },
            { "Kapat", "Close" },

            // primary and secondary actions
            { "Kur ve Bağlan", "Install & Connect" },
            { "Onar", "Repair" },
            { "Yeniden Bağlan", "Reconnect" },
            { "Elle Kur (WarpCord.bat)", "Install by hand (WarpCord.bat)" },
            { "Durum", "Status" },
            { "Tanı", "Diagnose" },
            { "Gelişmiş", "Advanced" },
            { "Kurulumu kaldır", "Uninstall" },
            { "Klasik kurulumu başlat", "Start the classic installer" },
            { "Nasıl kapatılır?", "How to turn it off?" },
            { "Yönetici olarak yeniden başlat", "Restart as administrator" },
            { "Denetle", "Check" },
            { "Ayarlar", "Settings" },

            // sections
            { "MOTOR", "ENGINE" },
            { "UÇ (İSTEĞE BAĞLI)", "ENDPOINT (OPTIONAL)" },
            { "KURULUM YÖNTEMİ", "INSTALL METHOD" },
            { "CANLI DURUM", "LIVE STATUS" },
            { "KAYITLAR", "LOGS" },
            { "NASIL ÇALIŞIR", "HOW IT WORKS" },

            // install method + options
            { "Uygulama ile", "With the app" },
            { "Elle (WarpCord.bat)", "By hand (WarpCord.bat)" },
            { "Yeni ücretsiz WARP cihazı kaydet", "Register a new free WARP device" },
            { "Sistem DNS'ine dokunma", "Leave the system DNS alone" },
            { "Kurulum bu pencerede çalışır; adımları canlı görürsün ve çıktı kaydın içinde kalır.", "The install runs in this window; steps stream live and stay in the log." },
            { "Kurulum, proje klasöründeki WarpCord.bat ile ayrı bir komut penceresinde çalışır. Bu pencere açık kalır ve durumu izler.", "The install runs WarpCord.bat in its own console; this window stays open and keeps watching." },
            { "Klasik WarpCord.bat bulunamadı (tek dosya modu): kurulum ve onarım bu pencerede çalışır.", "WarpCord.bat not found (single-file mode): install and repair run in this window." },

            // steps
            { "Tüneli kur", "Install the tunnel" },
            { "sing-box + wintun indirilir, ücretsiz WARP cihazı kaydedilir, çalışan motor seçilir.", "sing-box + wintun are fetched, a free WARP device is registered, the working engine is picked." },
            { "Yalnızca Discord tünellenir", "Only Discord is tunnelled" },
            { "discord.com ve Discord.exe akışları tünelden çıkar; DNS tünelin içinde çözülür.", "discord.com and Discord.exe flows leave through the tunnel; DNS is resolved inside it." },
            { "Geri kalan her şey doğrudan", "Everything else stays direct" },
            { "Oyunlar, indirmeler ve yerel servisler etkilenmez; ping ve IP değişmez.", "Games, downloads and local services are untouched; ping and IP stay the same." },
            { "Tünel günlüğü", "Tunnel log" },
            { "Onarım günlüğü", "Repair log" },
            { "tunnel.log dosyasını aç", "Open tunnel.log" },
            { "heal.log dosyasını aç", "Open heal.log" },

            // stat rows
            { "Mod", "Mode" },
            { "Uç", "Endpoint" },
            { "Onarım", "Repair" },
            { "Dış IP", "Public IP" },
            { "DNS sunucusu", "DNS server" },
            { "IPv6", "IPv6" },
            { "Ses (UDP)", "Voice (UDP)" },
            { "Trafik", "Traffic" },
            { "engelli", "blocked" },

            // pills and status details
            { "KONTROL EDİLİYOR", "CHECKING" },
            { "SAĞLIKLI", "HEALTHY" },
            { "SORUN VAR", "PROBLEM" },
            { "KURULU DEĞİL", "NOT INSTALLED" },
            { "Kurulu değil", "Not installed" },
            { "WarpCord bu bilgisayarda kurulu değil. Kurulum: ücretsiz WARP cihazı kaydedilir, uçlar denenir, çalışan motor seçilir ve tünel açılır.\nİlk kurulum 1–2 dakika sürer.", "WarpCord is not installed on this machine. Setup registers a free WARP device, probes endpoints, picks the working engine and opens the tunnel.\nThe first install takes 1–2 minutes." },

            // logs
            { "Kopyala", "Copy" },
            { "Temizle", "Clear" },
            { "kaydır", "follow" },
            { "İptal", "Cancel" },
            { "İşlem", "Action" },
            { "Tünel", "Tunnel" },
            { "Kurulum, onarım ve teşhis çıktısı burada akar. Başlamak için soldaki düğmeyi kullan.", "Install, repair and diagnostics output streams here. Use the button on the left to start." },
            { "çalışıyor", "running" },
            { "tamam", "done" },
            { "hata", "error" },
            { "elle", "manual" },
            { "kopyalandı", "copied" },
            { "Tünel günlüğü boş. Kurulumdan sonra sing-box akışları burada görünür.", "The tunnel log is empty. sing-box flows appear here after install." },
            { "Onarım günlüğü boş. Koruyucu çalıştığında buraya yazar.", "The repair log is empty. The guardian writes here when it runs." },
            { "Şimdi yenile", "Refresh now" },
            { "son", "at" },

            // banners
            { "Smart App Control açık: imzasız WarpCord.exe ve sing-box.exe bu makinede engellenir.", "Smart App Control is on: unsigned WarpCord.exe and sing-box.exe are blocked here." },
            { "Smart App Control değerlendirme modunda: imzasız bileşenler engellenebilir.", "Smart App Control is in evaluation mode: unsigned components may be blocked." },
            { "Kurulum, onarım ve kaldırma yönetici hakları ister. Durum bilgisi yönetici olmadan da okunur.", "Install, repair and uninstall need administrator rights. Status still works without them." },
            { "Kurulum ve onarım için yönetici hakları gerekiyor.", "Administrator rights are required to install and repair." },
            { "Ağ değişti: DNS ve tünel yenilenmeli.", "The network changed: DNS and the tunnel should be refreshed." },
            { "Şimdi yenile", "Refresh" },

            // status bar
            { "yönetici", "administrator" },
            { "yönetici değil", "not administrator" },
            { "Klasörü aç", "Open folder" },
            { "gömülü motor", "embedded engine" },
            { "motor bulunamadı (app\\install.ps1 yok)", "engine not found (no app\\install.ps1)" },

            // discord helper
            { "Discord'u yeniden başlat", "Restart Discord" },
            { "Discord kurulu değil ya da yol bulunamadı.", "Discord is not installed or its path was not found." },
            { "Discord yeniden başlatıldı.", "Discord restarted." },

            // tray
            { "WarpCord'u aç", "Open WarpCord" },
            { "WarpCord", "WarpCord" },
            { "Çıkış", "Quit" },
            { "Tünel sağlıklı", "Tunnel is healthy" },
            { "Tünelde sorun var", "The tunnel has a problem" },
            { "Durum okunuyor", "Reading status" },
            { "Tepside çalış", "Run in the tray" },

            // settings window
            { "Genel", "General" },
            { "Hazırlık", "Readiness" },
            { "Uçlar", "Endpoints" },
            { "Hakkında", "About" },
            { "Dil", "Language" },
            { "Türkçe", "Turkish" },
            { "İngilizce", "English" },
            { "Tepsi simgesi", "Tray icon" },
            { "Bildirimler", "Notifications" },
            { "Küçültünce tepsiye in", "Minimise to the tray" },
            { "Otomatik onarım", "Automatic repair" },
            { "Sorun görüldüğünde onarımı kendisi çalıştırır.", "Runs a repair by itself when a problem is detected." },
            { "Taşınabilir mod", "Portable mode" },
            { "Açık: ayarlar ve motor exe'nin yanında tutulur.", "On: settings and the engine stay next to the exe." },
            { "Hazırlık denetimi bu makinede kurulumu engelleyen bir şey var mı diye bakar.", "The readiness check looks for anything on this machine that would block an install." },
            { "Uçları dene", "Probe endpoints" },
            { "En iyisini kullan", "Use the best one" },
            { "Bu ucu kullan", "Use this endpoint" },
            { "Sürümü denetle", "Check for updates" },
            { "Tanı paketi", "Diagnostics bundle" },
            { "Sürüm", "Version" },
            { "Arayüz", "UI" },
            { "Depo", "Repository" },
            { "Güncel", "Up to date" },
            { "Yeni sürüm var", "New version available" },
            { "Denetleniyor…", "Checking…" },
            { "Denetim başarısız", "Check failed" },

            // readiness rows
            { "Yönetici", "Administrator" },
            { "Dil modu", "Language mode" },
            { "Yürütme politikası", "Execution policy" },
            { "GPO kısıtı", "GPO restriction" },
            { "Defender", "Defender" },
            { "Gerçek zamanlı koruma", "Real-time protection" },
            { "ASR kuralı", "ASR rule" },
            { "Korumalı klasörler", "Controlled folders" },
            { "Smart App Control", "Smart App Control" },
            { "WDAC/UMCI", "WDAC/UMCI" },
            { "Mark-of-the-web", "Mark-of-the-web" },
            { "yok", "none" },
            { "var", "yes" },
            { "açık", "on" },
            { "kapalı", "off" },
            { "değerlendirme", "evaluation" },
            { "kapalı (iyi)", "off (good)" },
            { "kısıtlı", "restricted" },

            // dialogs
            { "Vazgeç", "Cancel" },
            { "Tamam", "OK" },
            { "Kurulum yöntemi", "Install method" },
            { "İşlem sürüyor", "Work in progress" },
            { "İşlemi iptal et", "Cancel the action" },
            { "Uygulama ile kur", "Install with the app" },
            { "Elle kur — WarpCord.bat", "Install by hand — WarpCord.bat" },
            { "ÖNERİLEN", "RECOMMENDED" },
            { "Adımları bu pencerede canlı görürsün; kurulum çıktısı kaydın içinde kalır.", "Watch the steps live in this window; the output stays in the log." },
            { "Klasik kurulum ayrı bir komut penceresinde çalışır; UAC istemi orada çıkar. Uygulama açık kalır ve durumu izlemeye devam eder.", "The classic installer runs in its own console; the UAC prompt appears there. This window stays open and keeps watching." },
            { "Seçimini sonra Gelişmiş bölümünden değiştirebilirsin.", "You can change this later under Advanced." },
            { "İki yol da aynı tüneli kurar; yalnızca kurulumun nerede çalıştığı değişir.", "Both routes install the same tunnel; only where it runs differs." },
            { "Görevler, güvenlik duvarı kuralları ve tünel dosyaları silinir; adaptör DNS'i geri alınır. Discord yeniden doğrudan bağlanmayı dener. Devam edilsin mi?", "Tasks, firewall rules and tunnel files are removed; the adapter DNS is restored. Discord goes back to connecting directly. Continue?" },
            { "Süreç ağacı zorla durdurulacak. Kurulum yarıda kalırsa tünel kapalı kalabilir; ardından Onar çalıştırabilirsiniz.", "The process tree will be killed. A half-finished install can leave the tunnel down; run Repair afterwards." },
            { "Devam eden işlem iptal edilecek. Uygulama kapatılsın mı?", "The running action will be cancelled. Close the app?" },
            { "İptal et", "Cancel it" }
        };

        private static readonly Dictionary<string, string> English = Build();

        private static Dictionary<string, string> Build()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < Pairs.GetLength(0); i++)
            {
                string key = Pairs[i, 0];
                if (!map.ContainsKey(key)) map[key] = Pairs[i, 1];
            }
            return map;
        }

        /// <summary>Translate one Turkish source string.</summary>
        public static string T(string turkish)
        {
            if (!IsEnglish || string.IsNullOrEmpty(turkish)) return turkish;
            string english;
            return English.TryGetValue(turkish, out english) ? english : turkish;
        }

        private static readonly string[,] Phrases =
        {
            { "WARP tüneli", "WARP tunnel" },
            { "DPI (TLS parçalama)", "DPI (TLS fragmentation)" },
            { "gerekmiyor (WARP modu)", "not needed (WARP mode)" },
            { "henüz çalışmadı", "not run yet" },
            { "sorun sürüyor", "still failing" },
            { "çözümlenemedi", "could not resolve" },
            { "erişilemiyor", "unreachable" },
            { "zehirlenmiş", "poisoned" },
            { "onarıldı", "repaired" },
            { "ÇALIŞMIYOR", "NOT RUNNING" },
            { "çalışıyor", "running" },
            { "kurulu değil", "not installed" },
            { "sağlıklı", "healthy" },
            { "görünmüyor", "missing" },
            { "ayakta", "up" },
            { "temiz", "clean" },
            { "kapalı", "down" },
            { "proxy portu", "proxy port" },
            { "uç ", "endpoint " },
            { "yok", "none" },
            { "Sorun:", "Problem:" },
            { "Onar düğmesi bozulan parçayı kendisi düzeltir.", "Repair fixes the broken part." },
            { "İlk kurulum 1–2 dakika sürer.", "The first install takes 1–2 minutes." },
            { "API gecikmesi", "API latency" },
            { "İmza", "Signature" },
            { "İndir", "Download" },
            { "İndiriliyor…", "Downloading…" },
            { "İndirildi ve doğrulandı", "Downloaded and verified" },
            { "İndirildi (özet yok, elle doğrula)", "Downloaded (no digest, verify manually)" },
            { "İndirme başarısız", "Download failed" },
            { "İndirme bağlantısı yok; sürümü yeniden denetle.", "No download link; run the version check again." },
            { "imzalı", "signed" },
            { "imzasız", "unsigned" },
            { "Smart App Control engelleyebilir", "Smart App Control may block it" },
            { "EK SÜREÇLER (DENEYSEL)", "EXTRA PROCESSES (EXPERIMENTAL)" },
            { "Bu süreçler de tünellenir (virgülle). Yeniden Bağlan ile uygulanır.", "These processes are tunnelled too (comma separated). Applied on Reconnect." },
            { "KURULUM", "INSTALL" },
            { "Bu makineye kur", "Install on this machine" },
            { "Kaldır", "Remove" },
            { "Kuruldu", "Installed" },
            { "Kaldırıldı", "Removed" },
            { "Kurulum başarısız", "Install failed" },
            { "Kaldırma başarısız", "Remove failed" },
            { "Bu makineye kurulu", "Installed on this machine" },
            { "Bu makineye kurulu değil. Kurulum, Windows Başlat menüsüne kısayol ekler (yönetici gerekmez).", "Not installed on this machine. Installing adds a Start menu shortcut (no admin needed)." },
            { "arayüz", "ui" }
        };

        /// <summary>Replace known Turkish phrases inside a composed value (status rows, details).</summary>
        public static string P(string text)
        {
            if (!IsEnglish || string.IsNullOrEmpty(text)) return text;
            string result = text;
            for (int i = 0; i < Phrases.GetLength(0); i++)
                result = result.Replace(Phrases[i, 0], Phrases[i, 1]);
            return result;
        }
        /// <summary>Translate then format.</summary>
        public static string F(string turkish, params object[] args)
        {
            return string.Format(T(turkish), args);
        }

        /// <summary>Swap every known literal in a loaded window (call after build, and on language change).</summary>
        public static void Apply(DependencyObject root)
        {
            if (root == null) return;
            ApplyElement(root);

            int count = VisualChildren(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                Apply(child);
            }
        }

        private static void ApplyElement(DependencyObject node)
        {
            var text = node as TextBlock;
            if (text != null && text.Inlines.Count <= 1)
            {
                string translated = T(text.Text);
                if (!ReferenceEquals(translated, text.Text) && translated != text.Text) text.Text = translated;
                return;
            }

            var button = node as Button;
            if (button != null)
            {
                string content = button.Content as string;
                if (content != null) button.Content = T(content);
                return;
            }

            var radio = node as RadioButton;
            if (radio != null)
            {
                string content = radio.Content as string;
                if (content != null) radio.Content = T(content);
                return;
            }

            var check = node as CheckBox;
            if (check != null)
            {
                string content = check.Content as string;
                if (content != null) check.Content = T(content);
            }
        }

        private static int VisualChildren(DependencyObject node)
        {
            try { return VisualTreeHelper.GetChildrenCount(node); }
            catch { return 0; }
        }
    }
}
