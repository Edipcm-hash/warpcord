using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace WarpCord
{
    /// <summary>
    /// One STUN binding request: proves outbound UDP works, which is what voice
    /// needs. Discord's media is UDP, so a broken UDP path is the usual reason
    /// "text works but voice does not".
    /// </summary>
    internal static class UdpProbe
    {
        private const string DefaultHost = "stun.l.google.com";

        public static async Task<long> StunAsync(int timeoutMs)
        {
            try
            {
                IPAddress address = await ResolveAsync(DefaultHost, timeoutMs);
                if (address == null) return -1;

                using (var client = new UdpClient())
                {
                    var request = new byte[20];
                    request[0] = 0x00;
                    request[1] = 0x01;                       // binding request
                    request[4] = 0x21;                       // magic cookie
                    request[5] = 0x12;
                    request[6] = 0xA4;
                    request[7] = 0x42;
                    var random = new Random();
                    for (int i = 8; i < 20; i++) request[i] = (byte)random.Next(256);

                    var endpoint = new IPEndPoint(address, 19302);
                    var watch = Stopwatch.StartNew();
                    await client.SendAsync(request, request.Length, endpoint);
                    Task<UdpReceiveResult> receive = client.ReceiveAsync();
                    Task finished = await Task.WhenAny(receive, Task.Delay(timeoutMs));
                    if (finished != receive) return -1;

                    UdpReceiveResult result = receive.Result;
                    watch.Stop();
                    byte[] data = result.Buffer;
                    if (data.Length >= 20 && data[0] == 0x01 && data[1] == 0x01)
                        return watch.ElapsedMilliseconds;
                }
            }
            catch
            {
                // a blocked or filtered UDP path is a valid answer, not a crash
            }
            return -1;
        }

        private static async Task<IPAddress> ResolveAsync(string host, int timeoutMs)
        {
            try
            {
                Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(host);
                Task finished = await Task.WhenAny(lookup, Task.Delay(timeoutMs));
                if (finished != lookup) return null;
                foreach (IPAddress address in await lookup)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork) return address;
                }
            }
            catch { }
            return null;
        }
    }
}
