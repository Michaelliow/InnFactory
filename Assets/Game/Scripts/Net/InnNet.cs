using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inn.Net
{
    /// <summary>
    /// 联机总控（主机权威，最多 5 人合作经营同一家客栈）。
    /// - 单机 / 主机：操作直接执行（和单机完全一样的代码路径）。
    /// - 客户端：操作打包发给主机，由主机用“这个掌柜”的身份执行；世界状态全部来自主机快照。
    /// 金币、名望、菜谱、员工、建筑是全队共享的；背包是每个掌柜自己的。
    /// 传输层见 ITransport（现在是局域网 LanTransport，以后可换 Steam）。
    /// </summary>
    public class InnNet : MonoBehaviour
    {
        public const float SnapshotHz = 15f;
        public const float PosHz = 15f;

        public ITransport transport;
        public bool IsHost => transport != null && transport.IsHost;
        public bool IsClient => transport != null && !transport.IsHost;
        public bool Online => transport != null;
        public int LocalId = 1;
        public string Status = "单机";
        public string LastError;
        public int snapshotsSent, snapshotsReceived, commandsExecuted;
        public string LocalName;
        public string InnName => G.player.playerName + "的客栈";

        LanDiscovery discovery;
        readonly Dictionary<int, Player> connPlayers = new Dictionary<int, Player>();   // 主机：连接 → 掌柜
        readonly HashSet<int> pendingConns = new HashSet<int>();                       // 主机：已连上还没打招呼
        float snapTimer, posTimer, announceTimer;
        int nextPlayerId = 2;
        bool welcomed;

        InnGame G => InnGame.I;

        void Awake()
        {
            LocalName = PlayerPrefs.GetString("InnPlayerName", "掌柜" + Random.Range(100, 999));
        }

        void Start()
        {
            // 命令行：-name 名字  -host [-port N]  -join IP [-port N]
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string k) { int i = System.Array.IndexOf(args, k); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            if (Arg("-name") != null) SetName(Arg("-name"));
            int port = int.TryParse(Arg("-port"), out var p) ? p : LanTransport.DefaultPort;
            if (args.Contains("-host")) Host(port);
            else if (Arg("-join") != null) Join(Arg("-join"), port);
        }

        public void SetName(string n)
        {
            n = (n ?? "").Trim();
            if (n.Length == 0) return;
            if (n.Length > 12) n = n.Substring(0, 12);
            LocalName = n;
            PlayerPrefs.SetString("InnPlayerName", n);
            if (G != null && G.player != null) G.player.playerName = n;
        }

        // ───────────────────────── 开房间 / 加入 / 断开 ─────────────────────────
        public bool Host(int port = LanTransport.DefaultPort)
        {
            if (Online) return false;
            var t = LanTransport.Host(port, NetProto.MaxPlayers - 1);
            if (t.Error != null) { LastError = t.Error; G.LocalToast(t.Error, 5); return false; }
            transport = t;
            LocalId = 1;
            G.player.netId = 1;
            G.player.slot = 0;
            Status = "主机";
            G.LocalToast($"已开房间（端口 {t.Port}），最多 {NetProto.MaxPlayers} 人", 5);
            return true;
        }

        public bool Join(string ip, int port = LanTransport.DefaultPort)
        {
            if (Online) return false;
            var t = LanTransport.Join(ip, port);
            if (t.Error != null) { LastError = t.Error; G.LocalToast("加入失败：" + t.Error, 5); return false; }
            transport = t;
            welcomed = false;
            Status = "连接中…";
            t.Broadcast(NetProto.Hello(LocalName));
            return true;
        }

        /// <summary>主机：关房间（继续单机玩）。客户端：退出 → 重新载入场景回到自己的单机客栈。</summary>
        public void Leave()
        {
            if (!Online) return;
            bool wasClient = IsClient;
            transport.Dispose();
            transport = null;
            if (wasClient) { SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); return; }
            foreach (var p in connPlayers.Values.ToList()) RemoveRemote(p);
            connPlayers.Clear(); pendingConns.Clear();
            Status = "单机";
            G.LocalToast("房间已关闭");
        }

        void OnDestroy()
        {
            transport?.Dispose();
            discovery?.Dispose();
        }

        public List<LanDiscovery.Found> FoundHosts()
        {
            if (discovery == null) discovery = LanDiscovery.Listen();
            return discovery.Hosts();
        }

        public void StopDiscovery() { discovery?.Dispose(); discovery = null; }

        // ───────────────────────── 每帧 ─────────────────────────
        void Update()
        {
            if (transport == null) return;
            if (IsHost) HostUpdate(); else ClientUpdate();
        }

        void HostUpdate()
        {
            while (transport.TryJoined(out int c)) pendingConns.Add(c);
            while (transport.TryLeft(out int c))
            {
                pendingConns.Remove(c);
                if (connPlayers.TryGetValue(c, out var p)) { connPlayers.Remove(c); G.Toast($"{p.playerName} 离开了"); RemoveRemote(p); }
            }
            while (transport.TryReceive(out int c, out var data))
            {
                try { HostReceive(c, data); }
                catch (System.Exception e) { Debug.LogWarning("[InnNet] 坏包 from " + c + ": " + e.Message); }
            }

            snapTimer += Time.unscaledDeltaTime;
            if (snapTimer >= 1f / SnapshotHz && connPlayers.Count > 0)
            {
                snapTimer = 0;
                transport.Broadcast(WriteSnapshot());
                snapshotsSent++;
            }
            announceTimer += Time.unscaledDeltaTime;
            if (announceTimer >= 1f)
            {
                announceTimer = 0;
                if (transport is LanTransport lan) LanDiscovery.Announce(InnName, lan.Port, G.players.Count, NetProto.MaxPlayers);
            }
            Status = $"主机 {G.players.Count}/{NetProto.MaxPlayers}";
        }

        void HostReceive(int conn, byte[] data)
        {
            var r = NetProto.Open(data, out var m);
            switch (m)
            {
                case NetProto.Msg.Hello:
                {
                    int ver = r.ReadInt32();
                    string name = r.ReadString();
                    if (connPlayers.ContainsKey(conn)) return;
                    if (ver != NetProto.Version) { transport.Send(conn, NetProto.Text(NetProto.Msg.Reject, $"版本不同（主机 {NetProto.Version}，你 {ver}）")); transport.Kick(conn); return; }
                    pendingConns.Remove(conn);
                    var p = SpawnRemote(nextPlayerId++, name, FreeSlot());
                    p.transform.position = p.netPos = G.ZoneSpawn(Zone.Kitchen) + new Vector3(0.4f * p.slot, 0, 0);
                    connPlayers[conn] = p;
                    transport.Send(conn, NetProto.Welcome(p.netId));
                    transport.Send(conn, WriteSnapshot());
                    G.Toast($"{p.playerName} 加入了客栈（{G.players.Count}/{NetProto.MaxPlayers}）");
                    break;
                }
                case NetProto.Msg.Pos:
                {
                    if (!connPlayers.TryGetValue(conn, out var p)) return;
                    p.netPos = new Vector3(r.ReadSingle(), 0, r.ReadSingle());
                    p.netRotY = r.ReadSingle();
                    break;
                }
                case NetProto.Msg.Cmd:
                {
                    if (!connPlayers.TryGetValue(conn, out var p)) return;
                    // 远端掌柜的位置以最新上报为准（交互距离检查用）
                    p.transform.position = p.netPos;
                    Execute(p, r);
                    break;
                }
            }
        }

        int FreeSlot()
        {
            for (int s = 0; s < NetProto.MaxPlayers; s++) if (!G.players.Any(p => p.slot == s)) return s;
            return 0;
        }

        Player SpawnRemote(int id, string name, int slot)
        {
            var go = new GameObject("Player " + name);
            var p = go.AddComponent<Player>();
            p.isLocal = false; p.netId = id; p.playerName = name;
            p.SetSlot(slot);
            G.players.Add(p);
            return p;
        }

        /// <summary>掌柜离开：背包里的东西放进最近的箱子（放不下就没了）</summary>
        void RemoveRemote(Player p)
        {
            if (!G.Replica)
                foreach (var s in p.bag.slots)
                {
                    var box = G.NearestContainer(p.transform.position, s.item);
                    if (box != null) box.bag.Add(s.item, s.count);
                }
            G.players.Remove(p);
            Destroy(p.gameObject);
        }

        void ClientUpdate()
        {
            while (transport.TryJoined(out _)) { }
            while (transport.TryReceive(out _, out var data))
            {
                try { ClientReceive(data); }
                catch (System.Exception e) { Debug.LogWarning("[InnNet] 坏包: " + e.Message + "\n" + e.StackTrace); }
            }
            if (transport == null) return;
            if (transport.TryLeft(out _) || !transport.Connected)
            {
                string why = LastError ?? "和主机断开了";
                transport.Dispose(); transport = null;
                Debug.Log("[InnNet] " + why);
                Disconnected?.Invoke(why);
                if (G.Replica) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                else G.LocalToast(why, 5);
                return;
            }
            posTimer += Time.unscaledDeltaTime;
            if (welcomed && posTimer >= 1f / PosHz)
            {
                posTimer = 0;
                transport.Broadcast(NetProto.Pos(G.player.transform.position, G.player.transform.eulerAngles.y));
            }
        }

        public System.Action<string> Disconnected;

        void ClientReceive(byte[] data)
        {
            var r = NetProto.Open(data, out var m);
            switch (m)
            {
                case NetProto.Msg.Welcome:
                    r.ReadInt32();
                    LocalId = r.ReadInt32();
                    G.BecomeReplica();
                    G.player.netId = LocalId;
                    welcomed = true;
                    Status = "已加入";
                    G.LocalToast("加入成功！", 4);
                    break;
                case NetProto.Msg.Snapshot:
                    if (!welcomed) return;
                    ReadSnapshot(r);
                    snapshotsReceived++;
                    break;
                case NetProto.Msg.Toast:
                    G.LocalToast(r.ReadString(), 4);
                    break;
                case NetProto.Msg.Full:
                case NetProto.Msg.Reject:
                    LastError = r.ReadString();
                    G.LocalToast(LastError, 5);
                    Rejected = true;
                    break;
            }
        }

        public bool Rejected;

        // ───────────────────────── 提示 ─────────────────────────
        public void SendToast(Player to, string text)
        {
            foreach (var kv in connPlayers) if (kv.Value == to) transport.Send(kv.Key, NetProto.Text(NetProto.Msg.Toast, text));
        }

        public void BroadcastToast(string text)
        {
            if (connPlayers.Count > 0) transport.Broadcast(NetProto.Text(NetProto.Msg.Toast, text));
        }

        // ───────────────────────── 操作指令 ─────────────────────────
        // 所有会改变共享世界的操作都走这里：单机 / 主机直接执行，客户端发给主机。
        BinaryWriter Cmd(NetProto.Cmd c, out MemoryStream ms)
        {
            var w = NetProto.Begin(NetProto.Msg.Cmd, out ms);
            w.Write((byte)c);
            return w;
        }

        void Send(MemoryStream ms)
        {
            var bytes = ms.ToArray();
            if (IsClient) { transport.Broadcast(bytes); return; }
            var r = NetProto.Open(bytes, out _);
            Execute(G.player, r);
        }

        public void Interact(Player p, Building b, KeyCode key)
        {
            var w = Cmd(NetProto.Cmd.Interact, out var ms);
            w.Write(b.netId); w.Write((byte)(key == KeyCode.F ? 1 : key == KeyCode.G ? 2 : 0)); w.Write(p.Selected);
            Send(ms);
        }

        public void Place(BuildableDef def, Vector2Int cell, int dir)
        {
            var w = Cmd(NetProto.Cmd.Place, out var ms);
            w.Write((short)Wire.IndexOf(G.db.buildables, def)); w.Write((short)cell.x); w.Write((short)cell.y); w.Write((byte)dir);
            Send(ms);
        }

        public void Remove(Vector2Int cell)
        {
            var w = Cmd(NetProto.Cmd.Remove, out var ms);
            w.Write((short)cell.x); w.Write((short)cell.y);
            Send(ms);
        }

        public void Hire(int recruitIndex, StaffJob job)
        {
            var w = Cmd(NetProto.Cmd.Hire, out var ms);
            w.Write((byte)recruitIndex); w.Write((byte)job);
            w.Write(recruitIndex < G.recruits.Count ? G.recruits[recruitIndex].name : "");
            Send(ms);
        }

        public void Fire(Staff s) { var w = Cmd(NetProto.Cmd.Fire, out var ms); w.Write(s.netId); Send(ms); }

        public void SetJob(Staff s, StaffJob job) { var w = Cmd(NetProto.Cmd.SetJob, out var ms); w.Write(s.netId); w.Write((byte)job); Send(ms); }

        public void BuySeed(CropDef c, int n) { var w = Cmd(NetProto.Cmd.BuySeed, out var ms); w.WriteCrop(c); w.Write((short)n); Send(ms); }

        public void ToggleDish(ItemDef d, bool sell) { var w = Cmd(NetProto.Cmd.ToggleDish, out var ms); w.WriteDef(d); w.Write(sell); Send(ms); }

        public void RefreshRecruits() { Cmd(NetProto.Cmd.RefreshRecruits, out var ms); Send(ms); }
        public void NextDay() { Cmd(NetProto.Cmd.NextDay, out var ms); Send(ms); }
        public void Pause() { Cmd(NetProto.Cmd.Pause, out var ms); Send(ms); }
        public void Speed() { Cmd(NetProto.Cmd.Speed, out var ms); Send(ms); }

        /// <summary>主机执行一条指令（actor = 发出指令的掌柜）</summary>
        void Execute(Player actor, BinaryReader r)
        {
            var c = (NetProto.Cmd)r.ReadByte();
            G.toastTarget = actor;
            try
            {
                commandsExecuted++;
                switch (c)
                {
                    case NetProto.Cmd.Interact:
                    {
                        int id = r.ReadInt32();
                        int key = r.ReadByte();
                        var sel = r.ReadItem();
                        var b = G.buildings.FirstOrDefault(x => x.netId == id);
                        if (b == null) return;
                        // 远端掌柜的距离检查放宽一点（网络延迟）
                        if (!actor.isLocal && b.DistanceTo(actor.transform.position) > G.db.interactRange + 1.5f) { G.Toast("太远了"); return; }
                        if (sel.Valid) actor.Select(sel); else actor.selected = -1;
                        if (key == 0) b.Interact(actor); else if (key == 1) b.Use(actor); else b.Configure(actor);
                        break;
                    }
                    case NetProto.Cmd.Place:
                    {
                        var def = Wire.At(G.db.buildables, r.ReadInt16());
                        var cell = new Vector2Int(r.ReadInt16(), r.ReadInt16());
                        G.Place(def, cell, r.ReadByte(), true);
                        break;
                    }
                    case NetProto.Cmd.Remove:
                    {
                        var b = G.grid.At(new Vector2Int(r.ReadInt16(), r.ReadInt16()));
                        if (b != null) G.Remove(b, actor);
                        break;
                    }
                    case NetProto.Cmd.Hire:
                    {
                        int idx = r.ReadByte(); var job = (StaffJob)r.ReadByte(); string name = r.ReadString();
                        // 用名字核对，防止两个人同时点同一个候选
                        if (idx < G.recruits.Count && G.recruits[idx].name == name) G.Hire(G.recruits[idx], job);
                        break;
                    }
                    case NetProto.Cmd.Fire:
                    {
                        int id = r.ReadInt32();
                        var s = G.staff.FirstOrDefault(x => x.netId == id);
                        if (s) G.Fire(s);
                        break;
                    }
                    case NetProto.Cmd.SetJob:
                    {
                        int id = r.ReadInt32(); var job = (StaffJob)r.ReadByte();
                        var s = G.staff.FirstOrDefault(x => x.netId == id);
                        if (s) s.SetJob(job);
                        break;
                    }
                    case NetProto.Cmd.BuySeed:
                    {
                        var crop = r.ReadCrop(); int n = r.ReadInt16();
                        if (crop != null && n > 0 && n <= 99) G.BuySeed(crop, n, actor);
                        break;
                    }
                    case NetProto.Cmd.ToggleDish:
                    {
                        var d = r.ReadItemDef(); bool sell = r.ReadBoolean();
                        if (d == null) break;
                        if (sell) G.disabledDishes.Remove(d);
                        else if (G.disabledDishes.Add(d)) foreach (var cu in G.customers) cu.DishWithdrawn(d);
                        break;
                    }
                    case NetProto.Cmd.RefreshRecruits: G.RefreshRecruits(true); break;
                    case NetProto.Cmd.NextDay: if (G.summary != null) G.NextDay(); break;
                    case NetProto.Cmd.Pause: G.userPaused = !G.userPaused; break;
                    case NetProto.Cmd.Speed: G.speedIndex = (G.speedIndex + 1) % InnGame.Speeds.Length; break;
                }
            }
            finally { G.toastTarget = null; }
        }

        // ───────────────────────── 快照 ─────────────────────────
        public byte[] WriteSnapshot()
        {
            var w = NetProto.Begin(NetProto.Msg.Snapshot, out var ms);
            w.Write(G.gold); w.Write(G.rep); w.Write(G.day); w.Write(G.clock); w.Write(G.Open);
            w.Write((byte)G.speedIndex); w.Write(G.userPaused); w.Write(Time.timeScale);
            w.Write(G.summary != null);
            if (G.summary != null)
            {
                var s = G.summary;
                w.Write(s.day); w.Write(s.income); w.Write(s.tips); w.Write(s.wages); w.Write(s.upkeep); w.Write(s.served); w.Write(s.left); w.Write(s.rep);
            }
            var known = G.KnownRecipes().ToList();
            w.Write((short)known.Count); foreach (var rcp in known) w.WriteRecipe(rcp);
            w.Write((short)G.disabledDishes.Count); foreach (var d in G.disabledDishes) w.WriteDef(d);
            w.Write((byte)G.recruits.Count); foreach (var d in G.recruits) w.Write(d);

            w.Write((short)G.buildings.Count);
            var sub = new MemoryStream();
            var sw = new BinaryWriter(sub);
            foreach (var b in G.buildings)
            {
                w.Write(b.netId); w.Write((short)Wire.IndexOf(G.db.buildables, b.def));
                w.Write((short)b.cell.x); w.Write((short)b.cell.y); w.Write((byte)b.dir);
                sub.SetLength(0);
                b.WriteState(sw);
                sw.Flush();
                w.Write((short)sub.Length); w.Write(sub.GetBuffer(), 0, (int)sub.Length);
            }

            w.Write((byte)G.players.Count);
            foreach (var p in G.players)
            {
                w.Write(p.netId); w.Write(p.playerName ?? ""); w.Write((byte)p.slot);
                var pos = p.isLocal ? p.transform.position : p.netPos;
                w.Write(pos.x); w.Write(pos.z); w.Write(p.isLocal ? p.transform.eulerAngles.y : p.netRotY);
                w.Write(p.bag);
            }

            var cs = G.customers.Where(c => c != null).ToList();
            w.Write((short)cs.Count);
            foreach (var c in cs)
            {
                w.Write(c.netId); w.Write((short)Wire.IndexOf(G.db.customers, c.def));
                var t = c.transform;
                w.Write(t.position.x); w.Write(t.position.y); w.Write(t.position.z); w.Write(t.eulerAngles.y);
                w.Write((byte)c.state); w.Write(c.waited);
                w.Write(c.seat != null ? c.seat.netId : 0);
                w.Write((byte)c.orders.Count); foreach (var o in c.orders) w.WriteDef(o);
                w.Write((byte)c.served.Count); foreach (var s in c.served) w.Write(s);
            }

            var st = G.staff.Where(s => s != null).ToList();
            w.Write((byte)st.Count);
            foreach (var s in st)
            {
                w.Write(s.netId); w.Write(s.data); w.Write((byte)s.job);
                w.Write(s.transform.position.x); w.Write(s.transform.position.z); w.Write(s.transform.eulerAngles.y);
                w.Write(s.status ?? "");
                w.Write((byte)s.carry.Count); foreach (var it in s.carry) w.Write(it);
            }
            return ms.ToArray();
        }

        void ReadSnapshot(BinaryReader r)
        {
            G.gold = r.ReadInt32(); G.rep = r.ReadInt32(); G.day = r.ReadInt32(); G.clock = r.ReadSingle(); G.Open = r.ReadBoolean();
            G.speedIndex = r.ReadByte(); G.userPaused = r.ReadBoolean(); G.replicaTimeScale = r.ReadSingle();
            if (r.ReadBoolean())
                G.summary = new DaySummary
                {
                    day = r.ReadInt32(), income = r.ReadInt32(), tips = r.ReadInt32(), wages = r.ReadInt32(), upkeep = r.ReadInt32(),
                    served = r.ReadInt32(), left = r.ReadInt32(), rep = r.ReadInt32(),
                };
            else G.summary = null;
            int n = r.ReadInt16();
            G.discovered.Clear();
            for (int i = 0; i < n; i++) { var rc = r.ReadRecipe(); if (rc) G.discovered.Add(rc); }
            n = r.ReadInt16();
            G.disabledDishes.Clear();
            for (int i = 0; i < n; i++) { var d = r.ReadItemDef(); if (d) G.disabledDishes.Add(d); }
            n = r.ReadByte();
            G.recruits.Clear();
            for (int i = 0; i < n; i++) G.recruits.Add(r.ReadStaff());

            // 建筑：先删掉主机那边没有了的，再建新的，再写状态
            n = r.ReadInt16();
            var entries = new List<(int id, BuildableDef def, Vector2Int cell, int dir, byte[] state)>(n);
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32();
                var def = Wire.At(G.db.buildables, r.ReadInt16());
                var cell = new Vector2Int(r.ReadInt16(), r.ReadInt16());
                int dir = r.ReadByte();
                var state = r.ReadBytes(r.ReadInt16());
                entries.Add((id, def, cell, dir, state));
            }
            var ids = new HashSet<int>(entries.Select(e => e.id));
            foreach (var b in G.buildings.Where(b => !ids.Contains(b.netId)).ToList()) G.RemoveReplica(b);
            var byId = G.buildings.ToDictionary(b => b.netId);
            foreach (var e in entries)
            {
                if (!byId.TryGetValue(e.id, out var b))
                {
                    b = G.Place(e.def, e.cell, e.dir, false, e.id);
                    if (b == null) continue;
                }
                if (e.state.Length > 0) b.ReadState(new BinaryReader(new MemoryStream(e.state)));
            }

            // 掌柜
            n = r.ReadByte();
            var seen = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32(); string name = r.ReadString(); int slot = r.ReadByte();
                var pos = new Vector3(r.ReadSingle(), 0, r.ReadSingle()); float rot = r.ReadSingle();
                seen.Add(id);
                Player p;
                if (id == LocalId) p = G.player;
                else
                {
                    p = G.players.FirstOrDefault(x => x.netId == id);
                    if (p == null) { p = SpawnRemote(id, name, slot); p.transform.position = pos; }
                    p.netPos = pos; p.netRotY = rot; p.playerName = name;
                }
                if (p.slot != slot) p.SetSlot(slot);
                r.ReadInto(p.bag);
            }
            foreach (var p in G.players.Where(x => !x.isLocal && !seen.Contains(x.netId)).ToList()) RemoveRemote(p);

            // 顾客
            n = r.ReadInt16();
            var custSeen = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32();
                var def = Wire.At(G.db.customers, r.ReadInt16());
                var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); float rot = r.ReadSingle();
                var state = (Customer.State)r.ReadByte(); float waited = r.ReadSingle();
                int seatId = r.ReadInt32();
                var orders = new List<ItemDef>(); int k = r.ReadByte(); for (int j = 0; j < k; j++) orders.Add(r.ReadItemDef());
                var served = new List<Item>(); k = r.ReadByte(); for (int j = 0; j < k; j++) served.Add(r.ReadItem());
                custSeen.Add(id);
                var c = G.customers.FirstOrDefault(x => x != null && x.netId == id);
                if (c == null)
                {
                    var go = new GameObject("Customer");
                    go.transform.position = pos;
                    c = go.AddComponent<Customer>();
                    c.SetupReplica(def, id);
                    G.customers.Add(c);
                }
                c.netPos = pos; c.netRotY = rot;
                var seat = seatId != 0 ? G.chairs.FirstOrDefault(x => x.netId == seatId) : null;
                c.ApplyReplica(state, orders, served, waited, seat);
            }
            foreach (var c in G.customers.Where(x => x == null || !custSeen.Contains(x.netId)).ToList())
            {
                G.customers.Remove(c);
                if (c) Destroy(c.gameObject);
            }

            // 员工
            n = r.ReadByte();
            var staffSeen = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int id = r.ReadInt32(); var data = r.ReadStaff(); var job = (StaffJob)r.ReadByte();
                var pos = new Vector3(r.ReadSingle(), 0, r.ReadSingle()); float rot = r.ReadSingle();
                string status = r.ReadString();
                var carry = new List<Item>(); int k = r.ReadByte(); for (int j = 0; j < k; j++) carry.Add(r.ReadItem());
                staffSeen.Add(id);
                var s = G.staff.FirstOrDefault(x => x != null && x.netId == id);
                if (s == null)
                {
                    var go = new GameObject("Staff");
                    go.transform.position = pos;
                    s = go.AddComponent<Staff>();
                    s.SetupReplica(data, job, id);
                    G.staff.Add(s);
                }
                s.netPos = pos; s.netRotY = rot;
                s.ApplyReplica(data, job, status, carry);
            }
            foreach (var s in G.staff.Where(x => x == null || !staffSeen.Contains(x.netId)).ToList())
            {
                G.staff.Remove(s);
                if (s) Destroy(s.gameObject);
            }
        }
    }
}
