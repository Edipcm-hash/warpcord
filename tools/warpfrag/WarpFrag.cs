// WarpFrag - a tiny local HTTP-CONNECT proxy that fragments the TLS
// ClientHello so ISP DPI cannot read the SNI in a single TCP segment.
//
// Why this exists: on some Turkish ISPs (Superonline in particular) Cloudflare
// WARP is blocked outright. The Discord block itself is an SNI filter, and the
// filter can be defeated by splitting the ClientHello inside the hostname.
// Discord.app itself never uses a proxy, so sing-box routes Discord's TCP
// flows into this proxy (http outbound -> 127.0.0.1), and everything else stays
// untouched.
//
// No third-party dependencies: System.Net.Sockets + System.Text.RegularExpressions.
// Built on Windows PowerShell 5.1 with Add-Type -OutputAssembly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class WarpFrag
{
    private static string _logPath;
    private static int _delayMs = 2;
    private static string _mode = "sni";
    private static string _dohServer = "1.1.1.1";
    private static bool _useDoh = true;

    private static void Main(string[] args)
    {
        int port = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--port": port = int.Parse(args[++i]); break;
                case "--log": _logPath = args[++i]; break;
                case "--mode": _mode = args[++i]; break;
                case "--delay": _delayMs = int.Parse(args[++i]); break;
                case "--doh": _dohServer = args[++i]; break;
                case "--no-doh": _useDoh = false; break;
                case "--help":
                    Console.Error.WriteLine("usage: warp-frag.exe --port <n> [--mode sni|sni2|byte|record|split4|tlsrec|tlsrec3] [--delay ms] [--log file] [--doh ip] [--no-doh]");
                    return;
                default:
                    Console.Error.WriteLine("unknown argument: " + a);
                    return;
            }
        }
        if (port <= 0)
        {
            Console.Error.WriteLine("missing --port");
            return;
        }

        // Keep the log bounded: rotate once at start when it gets large.
        if (!string.IsNullOrEmpty(_logPath))
        {
            try
            {
                FileInfo info = new FileInfo(_logPath);
                if (info.Exists && info.Length > 5 * 1024 * 1024)
                {
                    string old = _logPath + ".old";
                    if (File.Exists(old)) { File.Delete(old); }
                    File.Move(_logPath, old);
                }
            }
            catch { }
        }

        Log("warp-frag starting on 127.0.0.1:" + port + " mode=" + _mode + " delay=" + _delayMs + "ms doh=" + (_useDoh ? _dohServer : "off"));        TcpListener listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start(128);
        while (true)
        {
            TcpClient client = null;
            try { client = listener.AcceptTcpClient(); }
            catch (Exception ex) { Log("accept failed: " + ex.Message); continue; }
            Thread thread = new Thread(new ParameterizedThreadStart(HandleClient));
            thread.IsBackground = true;
            thread.Start(client);
        }
    }

    private static void Log(string message)
    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message;
        if (!string.IsNullOrEmpty(_logPath))
        {
            try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { }
        }
    }

    private static void HandleClient(object state)
    {
        TcpClient client = (TcpClient)state;
        TcpClient remote = null;
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;
            NetworkStream clientStream = client.GetStream();

            string header = ReadHeader(clientStream);
            if (header == null)
            {
                client.Close();
                return;
            }

            string[] parts = header.Split(' ');
            if (parts.Length < 2 || !parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                WriteAll(clientStream, Encoding.ASCII.GetBytes("HTTP/1.1 405 Method Not Allowed\r\nConnection: close\r\n\r\n"));
                client.Close();
                return;
            }

            string host;
            int port;
            if (!SplitHostPort(parts[1], out host, out port))
            {
                WriteAll(clientStream, Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n"));
                client.Close();
                return;
            }

            IPAddress address = Resolve(host);
            if (address == null)
            {
                Log("CONNECT " + host + ":" + port + " -> resolve failed");
                WriteAll(clientStream, Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n"));
                client.Close();
                return;
            }

            remote = new TcpClient(address.AddressFamily);
            remote.NoDelay = true;
            remote.ReceiveTimeout = 30000;
            remote.SendTimeout = 30000;
            IAsyncResult ar = remote.BeginConnect(address, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(8000))
            {
                Log("CONNECT " + host + ":" + port + " -> connect timeout");
                WriteAll(clientStream, Encoding.ASCII.GetBytes("HTTP/1.1 504 Gateway Timeout\r\nConnection: close\r\n\r\n"));
                client.Close();
                return;
            }
            remote.EndConnect(ar);

            WriteAll(clientStream, Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"));
            NetworkStream remoteStream = remote.GetStream();

            byte[] first = ReadFirstRecord(clientStream);
            if (first != null && first.Length > 0)
            {
                Log("CONNECT " + host + ":" + port + " -> " + address + " first=" + first.Length + "B");
                SendFragmented(remoteStream, first);
            }

            Relay(client, remote);
        }
        catch (Exception ex)
        {
            Log("connection error: " + ex.Message);
            try { client.Close(); } catch { }
            if (remote != null) { try { remote.Close(); } catch { } }
        }
    }

    private static void Relay(TcpClient client, TcpClient remote)
    {
        Thread up = new Thread(delegate()
        {
            Pump(client, remote);
        });
        up.IsBackground = true;
        up.Start();
        Pump(remote, client);
    }

    private static void Pump(TcpClient from, TcpClient to)
    {
        byte[] buffer = new byte[32768];
        try
        {
            NetworkStream input = from.GetStream();
            NetworkStream output = to.GetStream();
            while (true)
            {
                int read = input.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                output.Write(buffer, 0, read);
            }
        }
        catch (Exception)
        {
            // normal on teardown
        }
        finally
        {
            try { from.Close(); } catch { }
            try { to.Close(); } catch { }
        }
    }

    private static string ReadHeader(NetworkStream stream)
    {
        StringBuilder builder = new StringBuilder();
        byte[] one = new byte[1];
        int total = 0;
        while (total < 8192)
        {
            int read = stream.Read(one, 0, 1);
            if (read <= 0)
            {
                return null;
            }
            builder.Append((char)one[0]);
            total++;
            if (builder.Length >= 4 && builder.ToString(builder.Length - 4, 4) == "\r\n\r\n")
            {
                return builder.ToString();
            }
        }
        return null;
    }

    private static bool SplitHostPort(string value, out string host, out int port)
    {
        host = null;
        port = 443;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }
        if (value[0] == '[')
        {
            int close = value.IndexOf(']');
            if (close < 0) { return false; }
            host = value.Substring(1, close - 1);
            if (close + 1 < value.Length && value[close + 1] == ':')
            {
                return int.TryParse(value.Substring(close + 2), out port);
            }
            return true;
        }
        int colon = value.LastIndexOf(':');
        if (colon < 0)
        {
            host = value;
            return true;
        }
        host = value.Substring(0, colon);
        return int.TryParse(value.Substring(colon + 1), out port);
    }

    private static IPAddress Resolve(string host)
    {
        IPAddress parsed;
        if (IPAddress.TryParse(host, out parsed))
        {
            return parsed;
        }
        if (_useDoh)
        {
            IPAddress viaDoh = ResolveViaDoh(host);
            if (viaDoh != null)
            {
                return viaDoh;
            }
        }
        try
        {
            IPAddress[] all = Dns.GetHostAddresses(host);
            foreach (IPAddress candidate in all)
            {
                if (candidate.AddressFamily == AddressFamily.InterNetwork)
                {
                    return candidate;
                }
            }
            if (all.Length > 0)
            {
                return all[0];
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static IPAddress ResolveViaDoh(string host)
    {
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url = "https://" + _dohServer + "/dns-query?name=" + Uri.EscapeDataString(host) + "&type=A";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Accept = "application/dns-json";
            request.Proxy = null;
            request.Timeout = 5000;
            request.ReadWriteTimeout = 5000;
            request.UserAgent = "warp-frag";
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                string json = reader.ReadToEnd();
                MatchCollection matches = Regex.Matches(json, "\"data\"\\s*:\\s*\"([0-9]{1,3}(?:\\.[0-9]{1,3}){3})\"");
                foreach (Match match in matches)
                {
                    IPAddress candidate;
                    if (IPAddress.TryParse(match.Groups[1].Value, out candidate) &&
                        candidate.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return candidate;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("doh lookup failed for " + host + ": " + ex.Message);
        }
        return null;
    }

    private static byte[] ReadFirstRecord(NetworkStream stream)
    {
        byte[] header = new byte[5];
        int got = ReadExact(stream, header, 0, 5);
        if (got <= 0)
        {
            return null;
        }
        if (got < 5)
        {
            return Slice(header, 0, got);
        }
        // TLS handshake record?
        if (header[0] != 0x16 || header[1] != 0x03)
        {
            return header;
        }
        int length = (header[3] << 8) | header[4];
        if (length < 4 || length > 65535)
        {
            return header;
        }
        byte[] body = new byte[length];
        int bodyGot = ReadExact(stream, body, 0, length);
        byte[] record = new byte[5 + bodyGot];
        Array.Copy(header, 0, record, 0, 5);
        Array.Copy(body, 0, record, 5, bodyGot);
        return record;
    }

    private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read <= 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }

    private static byte[] Slice(byte[] source, int offset, int count)
    {
        byte[] result = new byte[count];
        Array.Copy(source, offset, result, 0, count);
        return result;
    }

    private static string JoinSizes(List<byte[]> records)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < records.Count; i++)
        {
            if (i > 0) { builder.Append('+'); }
            builder.Append(records[i].Length);
        }
        return builder.ToString();
    }

    private static void SendFragmented(NetworkStream stream, byte[] payload)
    {
        if (_mode.StartsWith("tlsrec", StringComparison.Ordinal))
        {
            List<byte[]> records = RewriteRecords(payload, _mode == "tlsrec3" ? 3 : 2);
            if (records != null && records.Count > 1)
            {
                Log("tlsrec: record " + payload.Length + "B -> " + records.Count + " records [" + JoinSizes(records) + "]");
                for (int i = 0; i < records.Count; i++)
                {
                    stream.Write(records[i], 0, records[i].Length);
                    stream.Flush();
                    if (_delayMs > 0 && i + 1 < records.Count)
                    {
                        Thread.Sleep(_delayMs);
                    }
                }
                return;
            }
            int[] sniBytes = FindSni(payload);
            Log("fragment: payload=" + payload.Length + "B sni=" + (sniBytes == null ? "yok" : (sniBytes[0] + "," + sniBytes[1])) + " mode=" + _mode);
        }
        List<int> cuts = ComputeCuts(payload);
        cuts.Sort();
        int previous = 0;
        for (int i = 0; i < cuts.Count; i++)
        {
            int cut = cuts[i];
            if (cut <= previous || cut >= payload.Length)
            {
                continue;
            }
            stream.Write(payload, previous, cut - previous);
            stream.Flush();
            if (_delayMs > 0)
            {
                Thread.Sleep(_delayMs);
            }
            previous = cut;
        }
        if (previous < payload.Length)
        {
            stream.Write(payload, previous, payload.Length - previous);
            stream.Flush();
        }
    }

    // Re-emits one TLS ClientHello record as 2 or 3 records. RFC 8446 allows a
    // handshake message to span records, so the server reassembles it happily,
    // while a DPI that parses records one by one never sees the whole SNI.
    private static List<byte[]> RewriteRecords(byte[] payload, int parts)
    {
        if (payload.Length < 16 || payload[0] != 0x16 || payload[1] != 0x03)
        {
            return null;
        }
        int bodyLength = payload.Length - 5;
        int[] sni = FindSni(payload);
        List<int> cuts = new List<int>();
        if (parts >= 3 && sni != null && sni[1] > 2)
        {
            cuts.Add(sni[0] - 5);
            cuts.Add(sni[0] - 5 + sni[1] / 2);
        }
        else if (sni != null && sni[1] > 2)
        {
            cuts.Add(sni[0] - 5 + sni[1] / 2);
        }
        else
        {
            cuts.Add(Math.Max(1, bodyLength / 2));
        }
        cuts.Sort();

        List<byte[]> records = new List<byte[]>();
        int previous = 0;
        byte minor = payload[2];
        for (int i = 0; i < cuts.Count; i++)
        {
            int cut = cuts[i];
            if (cut <= previous || cut >= bodyLength)
            {
                continue;
            }
            int length = cut - previous;
            byte[] record = new byte[5 + length];
            record[0] = 0x16;
            record[1] = 0x03;
            record[2] = minor;
            record[3] = (byte)((length >> 8) & 0xFF);
            record[4] = (byte)(length & 0xFF);
            Array.Copy(payload, 5 + previous, record, 5, length);
            records.Add(record);
            previous = cut;
        }
        if (previous < bodyLength)
        {
            int length = bodyLength - previous;
            byte[] record = new byte[5 + length];
            record[0] = 0x16;
            record[1] = 0x03;
            record[2] = minor;
            record[3] = (byte)((length >> 8) & 0xFF);
            record[4] = (byte)(length & 0xFF);
            Array.Copy(payload, 5 + previous, record, 5, length);
            records.Add(record);
        }
        return records.Count > 1 ? records : null;
    }

    private static List<int> ComputeCuts(byte[] payload)
    {
        List<int> cuts = new List<int>();
        int[] sni = FindSni(payload); // {start, length}
        switch (_mode)
        {
            case "byte":
                cuts.Add(1);
                break;
            case "record":
                cuts.Add(5);
                break;
            case "sni2":
                if (sni != null)
                {
                    cuts.Add(sni[0]);
                    cuts.Add(sni[0] + Math.Max(1, sni[1] / 2));
                }
                else
                {
                    cuts.Add(5);
                }
                break;
            case "split4":
                if (sni != null)
                {
                    cuts.Add(5);
                    cuts.Add(sni[0] + Math.Max(1, sni[1] / 3));
                    cuts.Add(sni[0] + Math.Max(2, (sni[1] * 2) / 3));
                }
                else
                {
                    cuts.Add(5);
                }
                break;
            default: // "sni"
                if (sni != null && sni[1] > 1)
                {
                    cuts.Add(sni[0] + sni[1] / 2);
                }
                else
                {
                    cuts.Add(1);
                }
                break;
        }
        return cuts;
    }

    // Returns {start of hostname, length} inside a TLS ClientHello, or null.
    private static int[] FindSni(byte[] b)
    {
        try
        {
            int pos = 5;
            if (b.Length < pos + 4 + 2 + 32 + 1)
            {
                return null;
            }
            if (b[pos] != 0x01)
            {
                return null;
            }
            pos += 4;
            pos += 2 + 32;
            int sessionLength = b[pos];
            pos += 1 + sessionLength;
            if (pos + 2 > b.Length) { return null; }
            int cipherLength = (b[pos] << 8) | b[pos + 1];
            pos += 2 + cipherLength;
            if (pos + 1 > b.Length) { return null; }
            int compressionLength = b[pos];
            pos += 1 + compressionLength;
            if (pos + 2 > b.Length) { return null; }
            int extensionsLength = (b[pos] << 8) | b[pos + 1];
            pos += 2;
            int end = Math.Min(b.Length, pos + extensionsLength);
            while (pos + 4 <= end)
            {
                int type = (b[pos] << 8) | b[pos + 1];
                int length = (b[pos + 2] << 8) | b[pos + 3];
                pos += 4;
                if (pos + length > end)
                {
                    return null;
                }
                if (type == 0)
                {
                    int cursor = pos;
                    int listLength = (b[cursor] << 8) | b[cursor + 1];
                    cursor += 2;
                    int listEnd = Math.Min(end, cursor + listLength);
                    while (cursor + 3 <= listEnd)
                    {
                        int nameType = b[cursor];
                        int nameLength = (b[cursor + 1] << 8) | b[cursor + 2];
                        cursor += 3;
                        if (nameType == 0)
                        {
                            return new int[] { cursor, nameLength };
                        }
                        cursor += nameLength;
                    }
                }
                pos += length;
            }
        }
        catch (IndexOutOfRangeException)
        {
        }
        return null;
    }

    private static void WriteAll(Stream stream, byte[] data)
    {
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }
}
