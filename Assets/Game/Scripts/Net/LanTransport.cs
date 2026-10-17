using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Inn.Net
{
    /// <summary>
    /// 局域网直连（移植自 XiuXianIdle 的 NetSession）。TCP：每条消息 = [int32 长度][内容]，后台线程收包进队列，主线程处理。
    /// 主机监听端口，最多 MaxClients 个客户端；客户端连主机 IP。外网可用 IP 直连 + 端口映射，或虚拟局域网工具。
    /// </summary>
    public class LanTransport : ITransport
    {
        public const int DefaultPort = 7790;

        public bool IsHost { get; private set; }
        public int Port { get; private set; }
        public string Error { get; private set; }
        public int MaxClients { get; private set; }

        class Conn
        {
            public int Id;
            public TcpClient Tcp;
            public NetworkStream Stream;
            public readonly object WriteLock = new object();
            public volatile bool Alive = true;
        }

        TcpListener listener;
        readonly Dictionary<int, Conn> conns = new Dictionary<int, Conn>();
        readonly ConcurrentQueue<(int conn, byte[] data)> inbox = new ConcurrentQueue<(int, byte[])>();
        readonly ConcurrentQueue<int> joined = new ConcurrentQueue<int>(), left = new ConcurrentQueue<int>();
        int nextId = 1;
        volatile bool disposed;

        public int ClientCount { get { lock (conns) return conns.Count; } }
        public bool Connected { get { lock (conns) return IsHost ? listener != null : conns.Count > 0; } }
        public string Address => IsHost ? string.Join(" / ", LocalIPs()) + ":" + Port : "";

        public static LanTransport Host(int port, int maxClients)
        {
            var s = new LanTransport { IsHost = true, Port = port, MaxClients = maxClients };
            try
            {
                s.listener = new TcpListener(IPAddress.Any, port);
                s.listener.Start();
                s.Port = ((IPEndPoint)s.listener.LocalEndpoint).Port;
                new Thread(s.AcceptLoop) { IsBackground = true, Name = "InnNetAccept" }.Start();
            }
            catch (Exception e) { s.listener = null; s.Error = "开主机失败：" + e.Message; }
            return s;
        }

        void AcceptLoop()
        {
            while (!disposed)
            {
                TcpClient tcp;
                try { tcp = listener.AcceptTcpClient(); }
                catch { return; }
                if (ClientCount >= MaxClients)
                {
                    // 满员：发一个满员包再断开，客户端能显示原因
                    try { var full = NetProto.Full(); var s = tcp.GetStream(); s.Write(BitConverter.GetBytes(full.Length), 0, 4); s.Write(full, 0, full.Length); s.Flush(); } catch { }
                    Thread.Sleep(200);
                    tcp.Close();
                    continue;
                }
                Add(tcp);
            }
        }

        public static LanTransport Join(string ip, int port, int timeoutMs = 3000)
        {
            var s = new LanTransport { IsHost = false, Port = port };
            var tcp = new TcpClient();
            try
            {
                var ar = tcp.BeginConnect(ip, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !tcp.Connected) { tcp.Close(); s.Error = "连接超时"; return s; }
                tcp.EndConnect(ar);
            }
            catch (Exception e) { tcp.Close(); s.Error = "连接失败：" + e.Message; return s; }
            s.Add(tcp);
            return s;
        }

        void Add(TcpClient tcp)
        {
            tcp.NoDelay = true;
            var c = new Conn { Tcp = tcp, Stream = tcp.GetStream() };
            lock (conns) { c.Id = nextId++; conns[c.Id] = c; }
            joined.Enqueue(c.Id);
            new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "InnNetRead" + c.Id }.Start();
        }

        void ReadLoop(Conn c)
        {
            var lenBuf = new byte[4];
            try
            {
                while (c.Alive && !disposed)
                {
                    ReadExactly(c.Stream, lenBuf, 4);
                    int len = BitConverter.ToInt32(lenBuf, 0);
                    if (len <= 0 || len > 8 * 1024 * 1024) throw new IOException("bad length " + len);
                    var data = new byte[len];
                    ReadExactly(c.Stream, data, len);
                    inbox.Enqueue((c.Id, data));
                }
            }
            catch { }
            Drop(c);
        }

        static void ReadExactly(Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0) throw new IOException("closed");
                got += r;
            }
        }

        void Drop(Conn c)
        {
            lock (conns)
            {
                c.Alive = false;
                if (!conns.Remove(c.Id)) return;
            }
            try { c.Tcp.Close(); } catch { }
            left.Enqueue(c.Id);
        }

        public bool TryJoined(out int id) => joined.TryDequeue(out id);
        public bool TryLeft(out int id) => left.TryDequeue(out id);

        public bool TryReceive(out int conn, out byte[] data)
        {
            if (inbox.TryDequeue(out var m)) { conn = m.conn; data = m.data; return true; }
            conn = 0; data = null;
            return false;
        }

        public void Send(int connId, byte[] msg)
        {
            Conn c;
            lock (conns) conns.TryGetValue(connId, out c);
            if (c != null) Write(c, msg);
        }

        public void Broadcast(byte[] msg)
        {
            Conn[] all;
            lock (conns) { all = new Conn[conns.Count]; conns.Values.CopyTo(all, 0); }
            foreach (var c in all) Write(c, msg);
        }

        void Write(Conn c, byte[] msg)
        {
            try
            {
                lock (c.WriteLock)
                {
                    c.Stream.Write(BitConverter.GetBytes(msg.Length), 0, 4);
                    c.Stream.Write(msg, 0, msg.Length);
                }
            }
            catch { Drop(c); }
        }

        public void Kick(int connId)
        {
            Conn c;
            lock (conns) conns.TryGetValue(connId, out c);
            if (c != null) Drop(c);
        }

        public void Dispose()
        {
            disposed = true;
            try { listener?.Stop(); } catch { }
            listener = null;
            Conn[] all;
            lock (conns) { all = new Conn[conns.Count]; conns.Values.CopyTo(all, 0); conns.Clear(); }
            foreach (var c in all) { c.Alive = false; try { c.Tcp.Close(); } catch { } }
        }

        public static List<string> LocalIPs()
        {
            var list = new List<string>();
            try
            {
                foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) list.Add(a.ToString());
            }
            catch { }
            return list;
        }
    }

    /// <summary>局域网发现：主机每秒向 255.255.255.255:7791 广播「INNFACTORY|客栈名|端口|人数|上限」，客户端监听收集。</summary>
    public class LanDiscovery : IDisposable
    {
        public const int Port = 7791;
        const string Tag = "INNFACTORY";

        public class Found
        {
            public string Ip, Name;
            public int Port, Players, Max;
            public DateTime Seen;
        }

        UdpClient udp;
        volatile bool disposed;
        readonly Dictionary<string, Found> found = new Dictionary<string, Found>();

        public static LanDiscovery Listen()
        {
            var d = new LanDiscovery();
            try
            {
                d.udp = new UdpClient();
                d.udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                d.udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                new Thread(d.Loop) { IsBackground = true, Name = "InnNetDiscovery" }.Start();
            }
            catch { d.udp = null; }
            return d;
        }

        void Loop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (!disposed)
            {
                try
                {
                    var data = udp.Receive(ref ep);
                    var p = Encoding.UTF8.GetString(data).Split('|');
                    if (p.Length < 5 || p[0] != Tag) continue;
                    lock (found)
                        found[ep.Address + ":" + p[2]] = new Found
                        {
                            Ip = ep.Address.ToString(), Name = p[1], Port = int.Parse(p[2]), Players = int.Parse(p[3]), Max = int.Parse(p[4]), Seen = DateTime.UtcNow,
                        };
                }
                catch { if (disposed) return; }
            }
        }

        public List<Found> Hosts()
        {
            var list = new List<Found>();
            lock (found)
                foreach (var f in found.Values)
                    if ((DateTime.UtcNow - f.Seen).TotalSeconds < 5) list.Add(f);
            return list;
        }

        static UdpClient sender;

        public static void Announce(string name, int port, int players, int max)
        {
            try
            {
                if (sender == null) sender = new UdpClient { EnableBroadcast = true };
                var msg = Encoding.UTF8.GetBytes($"{Tag}|{name.Replace("|", "")}|{port}|{players}|{max}");
                sender.Send(msg, msg.Length, new IPEndPoint(IPAddress.Broadcast, Port));
            }
            catch { }
        }

        public void Dispose()
        {
            disposed = true;
            try { udp?.Close(); } catch { }
        }
    }
}
