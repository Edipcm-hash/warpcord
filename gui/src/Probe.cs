using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace WarpCord
{
    /// <summary>One reading of the tunnel's health, cheap parts only or full.</summary>
    internal sealed class Snapshot
    {
        public bool Installed;
        public string Version;
        public string InstalledAt;
        public string Mode = "";
        public string Arch;
        public string SingBoxVersion;
        public string EndpointAddress;
        public int EndpointPort;
        public int FragPort;

        public bool SingBoxRunning;
        public int SingBoxPid;
        public bool FragRunning;
        public int FragPid;

        public bool TunUp;
        public string TunName;
        public string AdapterDns;
        public bool Ipv6Enabled;

        public int TunnelHits;
        public int DirectHits;

        public string HealStatus;
        public string HealLastRun;
        public string HealRepair;

        public bool NetworkChecked;
        public bool DnsChecked;
        public bool DnsOk;
        public bool DnsPoisoned;
        public string DnsText = "—";
        public bool VoiceChecked;
        public bool VoiceOk;
        public long VoiceMs;
        public string VoiceText = "—";
        public bool TrafficOk;
        public long TrafficUp;
        public long TrafficDown;
        public string TrafficText = "—";
        public bool ApiChecked;
        public bool ApiOk;
        public long ApiMs;
        public string ApiText = "—";
        public string PublicIp;

        public bool Healthy;
        public string Error;

        public bool DpiMode
        {
            get { return string.Equals(Mode, "dpi", StringComparison.OrdinalIgnoreCase); }
        }

        public string ModeName
        {
            get
            {
                if (DpiMode) return "DPI (TLS parçalama)";
                if (string.Equals(Mode, "warp", StringComparison.OrdinalIgnoreCase)) return "WARP tüneli";
                return string.IsNullOrEmpty(Mode) ? "—" : Mode;
            }
        }
    }

    /// <summary>Collects a <see cref="Snapshot"/> without blocking the UI thread.</summary>
    internal static class Prober
    {
        private static readonly HttpClient Http = CreateClient();
        private static Snapshot _cache = new Snapshot();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            handler.UseProxy = false;
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WarpCord-GUI/1.0");
            return client;
        }

        public static async Task<Snapshot> CollectAsync(bool network, bool publicIp)
        {
            var snap = new Snapshot();
            try
            {
                Cheap(snap);
            }
            catch (Exception ex)
            {
                snap.Error = ex.Message;
            }

            try { await TrafficAsync(snap); } catch { }

            if (network)
            {
                await NetworkAsync(snap, publicIp);
                _cache = snap;
            }
            else
            {
                snap.NetworkChecked = _cache.NetworkChecked;
                snap.DnsChecked = _cache.DnsChecked;
                snap.DnsOk = _cache.DnsOk;
                snap.DnsPoisoned = _cache.DnsPoisoned;
                snap.DnsText = _cache.DnsText;
                snap.ApiChecked = _cache.ApiChecked;
                snap.ApiOk = _cache.ApiOk;
                snap.ApiMs = _cache.ApiMs;
                snap.ApiText = _cache.ApiText;
                snap.PublicIp = _cache.PublicIp;
            }

            snap.Healthy = snap.Installed && snap.SingBoxRunning && snap.DnsOk && snap.ApiOk &&
                           (!snap.DpiMode || snap.FragRunning);
            return snap;
        }

        // ------------------------------------------------------------- cheap --
        private static void Cheap(Snapshot snap)
        {
            object state = Json.Node(Paths.StatePath);
            snap.Installed = state != null;
            if (snap.Installed)
            {
                snap.Version = Json.Str(state, "version");
                snap.InstalledAt = Json.Str(state, "installedAt");
                snap.Mode = Json.Str(state, "mode") ?? "";
                snap.Arch = Json.Str(state, "arch");
                snap.SingBoxVersion = Json.Str(state, "singBoxVersion");
                snap.EndpointAddress = Json.Str(state, "endpoint", "Address");
                snap.EndpointPort = Json.Int(state, 0, "endpoint", "Port");
                snap.FragPort = Json.Int(state, 0, "fragPort");
            }

            int pid;
            if (TryFindProcess("sing-box", out pid))
            {
                snap.SingBoxRunning = true;
                snap.SingBoxPid = pid;
            }
            if (TryFindProcess("warp-frag", out pid))
            {
                snap.FragRunning = true;
                snap.FragPid = pid;
            }

            try
            {
                var tun = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
                    string.Equals(n.Name, "WarpCord", StringComparison.OrdinalIgnoreCase));
                if (tun != null)
                {
                    snap.TunName = tun.Name;
                    snap.TunUp = tun.OperationalStatus == OperationalStatus.Up;
                }
            }
            catch
            {
                // adapter enumeration is best-effort
            }

            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                !string.Equals(n.Name, "WarpCord", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var primary = adapters.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Count > 0) ??
                              adapters.FirstOrDefault();
                if (primary != null)
                {
                    var dns = primary.GetIPProperties().DnsAddresses
                        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.ToString())
                        .Take(2)
                        .ToArray();
                    if (dns.Length > 0) snap.AdapterDns = string.Join(", ", dns);
                }

                snap.Ipv6Enabled = System.Net.Sockets.Socket.OSSupportsIPv6 &&
                                   NetworkInterface.GetAllNetworkInterfaces().Any(n =>
                                       n.OperationalStatus == OperationalStatus.Up &&
                                       n.GetIPProperties().UnicastAddresses.Any(a =>
                                           a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
                                           !a.Address.IsIPv6LinkLocal));
            }
            catch
            {
                // adapter details are best effort
            }
            object heal = Json.Node(Paths.HealStatePath);
            if (heal != null)
            {
                snap.HealStatus = Json.Str(heal, "status");
                snap.HealLastRun = Json.Str(heal, "lastRun");
                var repairs = Json.Get(heal, "repairs") as System.Collections.IList;
                if (repairs != null && repairs.Count > 0)
                    snap.HealRepair = Convert.ToString(repairs[repairs.Count - 1]);
            }

            CountTunnelLog(snap);
        }

        private static bool TryFindProcess(string name, out int pid)
        {
            pid = 0;
            try
            {
                Process[] found = Process.GetProcessesByName(name);
                if (found.Length == 0) return false;
                pid = found[0].Id;
                for (int i = 0; i < found.Length; i++) found[i].Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CountTunnelLog(Snapshot snap)
        {
            try
            {
                string[] lines = Tail(Paths.TunnelLogPath, 80);
                foreach (string line in lines)
                {
                    if (line.IndexOf("wireguard[warp]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("http[frag]", StringComparison.OrdinalIgnoreCase) >= 0)
                        snap.TunnelHits++;
                    else if (line.IndexOf("outbound/direct", StringComparison.OrdinalIgnoreCase) >= 0)
                        snap.DirectHits++;
                }
            }
            catch
            {
                // log may be locked while sing-box rotates it
            }
        }

        public static string[] Tail(string path, int count)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return new string[0];
            var text = Json.ReadAllText(path);
            if (string.IsNullOrEmpty(text)) return new string[0];
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int start = Math.Max(0, lines.Length - count);
            var result = new List<string>();
            for (int i = start; i < lines.Length; i++)
            {
                if (lines[i].Length > 0) result.Add(lines[i]);
            }
            return result.ToArray();
        }

        // ----------------------------------------------------------- network --
        private static async Task NetworkAsync(Snapshot snap, bool publicIp)
        {
            snap.NetworkChecked = true;

            // DNS: the ISP block page answers for discord.com on a poisoned line
            string[] addresses = await ResolveAsync("discord.com", 5000);
            snap.DnsChecked = true;
            if (addresses == null || addresses.Length == 0)
            {
                snap.DnsOk = false;
                snap.DnsText = "çözümlenemedi";
            }
            else
            {
                string first = addresses[0];
                string blocked = addresses.FirstOrDefault(IsPoisoned);
                snap.DnsPoisoned = blocked != null;
                snap.DnsOk = !snap.DnsPoisoned;
                if (snap.DnsPoisoned)
                {
                    snap.DnsText = "zehirlenmiş: " + blocked;
                }
                else
                {
                    string ipv4 = addresses.FirstOrDefault(a => a.IndexOf(':') < 0) ?? first;
                    snap.DnsText = ipv4 + (addresses.Length > 1 ? " +" + (addresses.Length - 1) : "");
                }
            }

            // Discord API: a real request through whatever path the tunnel set up
            var watch = Stopwatch.StartNew();
            try
            {
                using (var response = await Http.GetAsync("https://discord.com/api/v9/gateway"))
                {
                    watch.Stop();
                    string body = await response.Content.ReadAsStringAsync();
                    snap.ApiMs = watch.ElapsedMilliseconds;
                    snap.ApiChecked = true;
                    snap.ApiOk = response.IsSuccessStatusCode &&
                                 body.IndexOf("url", StringComparison.OrdinalIgnoreCase) >= 0;
                    snap.ApiText = snap.ApiOk
                        ? snap.ApiMs + " ms · HTTP " + (int)response.StatusCode
                        : "HTTP " + (int)response.StatusCode;
                }
            }
            catch (Exception ex)
            {
                watch.Stop();
                snap.ApiChecked = true;
                snap.ApiOk = false;
                snap.ApiMs = watch.ElapsedMilliseconds;
                snap.ApiText = "erişilemiyor · " + Shorten(ex.Message, 46);
            }

            long voiceMs = await UdpProbe.StunAsync(3000);
            snap.VoiceChecked = true;
            snap.VoiceOk = voiceMs >= 0;
            snap.VoiceMs = voiceMs;
            snap.VoiceText = snap.VoiceOk ? voiceMs + " ms" : L10n.T("engelli");

            if (publicIp)
            {
                try
                {
                    string trace = await Http.GetStringAsync("https://1.1.1.1/cdn-cgi/trace");
                    foreach (string line in trace.Replace("\r\n", "\n").Split('\n'))
                    {
                        if (line.StartsWith("ip=")) { snap.PublicIp = line.Substring(3).Trim(); break; }
                    }
                }
                catch
                {
                    // public IP is decorative
                }
            }
            else
            {
                snap.PublicIp = _cache.PublicIp;
            }
        }

        /// <summary>Live bytes from the localhost Clash API (sing-box), if exposed.</summary>
        private static async Task TrafficAsync(Snapshot snap)
        {
            if (!snap.Installed) return;
            try
            {
                string body = await Http.GetStringAsync("http://127.0.0.1:9099/connections");
                object node = Json.Parse(body);
                long up = ToLong(Json.Get(node, "uploadTotal"));
                long down = ToLong(Json.Get(node, "downloadTotal"));
                snap.TrafficOk = true;
                snap.TrafficUp = up;
                snap.TrafficDown = down;
                snap.TrafficText = "↑ " + FormatBytes(up) + " · ↓ " + FormatBytes(down);
            }
            catch
            {
                snap.TrafficOk = false;
                snap.TrafficText = "—";
            }
        }

        private static long ToLong(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt64(value); }
            catch { return 0; }
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }
        private static async Task<string[]> ResolveAsync(string host, int timeoutMs)
        {
            try
            {
                Task<IPAddress[]> task = Dns.GetHostAddressesAsync(host);
                Task finished = await Task.WhenAny(task, Task.Delay(timeoutMs));
                if (finished != task) return null;
                IPAddress[] addresses = await task;
                return addresses.Select(a => a.ToString()).ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsPoisoned(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;
            if (address == "195.175.254.2") return true;   // documented TTNet block page
            if (address.StartsWith("195.175.254.")) return true;
            if (address == "0.0.0.0" || address == "127.0.0.1") return true;
            return false;
        }

        public static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max - 1) + "…";
        }
    }
}
