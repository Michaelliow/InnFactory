using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Inn
{
    /// <summary>IMGUI 原型界面：顶栏、提示、背包栏、头顶文字、建造栏、菜谱 / 招募 / 商店面板、每日结算</summary>
    public class InnHUD : MonoBehaviour
    {
        const float RefHeight = 900f;
        GUIStyle label, small, title, box, bubble, button, slot, slotSel, toast, center;
        float scale;
        float W => Screen.width / scale;
        float H => Screen.height / scale;
        readonly List<Rect> uiRects = new List<Rect>();
        Vector2 scroll;

        InnGame G => InnGame.I;

        void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            label.normal.textColor = Color.white;
            small = new GUIStyle(label) { fontSize = 13 };
            title = new GUIStyle(label) { fontSize = 20, fontStyle = FontStyle.Bold };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            box = new GUIStyle(GUI.skin.box);
            box.normal.background = Tex(new Color(0.12f, 0.09f, 0.07f, 0.88f));
            bubble = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(6, 6, 2, 2) };
            bubble.normal.background = Tex(new Color(1f, 0.97f, 0.9f, 0.92f));
            bubble.normal.textColor = new Color(0.2f, 0.12f, 0.05f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, richText = true };
            slot = new GUIStyle(GUI.skin.button) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            slotSel = new GUIStyle(slot);
            slotSel.normal.background = Tex(new Color(0.85f, 0.6f, 0.2f, 1f));
            slotSel.normal.textColor = Color.black;
            toast = new GUIStyle(center) { fontSize = 17 };
            toast.normal.background = Tex(new Color(0f, 0f, 0f, 0.6f));
        }

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        void OnGUI()
        {
            if (G == null || G.player == null) return;
            Styles();
            scale = Screen.height / RefHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            if (Event.current.type == EventType.Layout) uiRects.Clear();

            WorldLabels();
            TopBar();
            Toasts();
            if (!G.build.active) { Prompt(); BagBar(); }
            else BuildPalette();
            switch (G.panel)
            {
                case InnGame.Panel.Recipes: RecipePanel(); break;
                case InnGame.Panel.Staff: StaffPanel(); break;
                case InnGame.Panel.Shop: ShopPanel(); break;
                case InnGame.Panel.Net: NetPanel(); break;
            }
            if (G.panel != InnGame.Panel.Net) G.net.StopDiscovery();
            InnInput.Typing = G.panel == InnGame.Panel.Net && GUIUtility.keyboardControl != 0;
            if (G.summary != null) Summary();

            var mouse = Event.current.mousePosition;
            G.build.pointerOverUI = uiRects.Any(r => r.Contains(mouse));
        }

        Rect UI(Rect r) { uiRects.Add(r); return r; }

        void TopBar()
        {
            GUI.Box(UI(new Rect(0, 0, W, 34)), GUIContent.none, box);
            string open = G.Open ? "营业中" : "<color=#ffb060>打烊</color>";
            string speed = G.userPaused ? "<color=#ff8080>暂停</color>" : $"×{InnGame.Speeds[G.speedIndex]}";
            var zone = Names.Of(G.grid.ZoneOf(G.player.transform.position));
            GUI.Label(new Rect(12, 5, W, 26),
                $"第 {G.day} 天  {G.ClockText}  {open}　│　<color=#ffd75e>金币 {G.gold}</color>　│　名望 Lv{G.RepLevel}（{G.rep}/{G.NextRepThreshold}）" +
                $"　│　{speed}　│　{zone}　│　员工 {G.staff.Count}/{G.StaffCap}　│　菜谱 {G.discovered.Count}/{G.db.recipes.Length}　│　客人 {G.customers.Count}　│　联机：{G.net.Status}", label);
            GUI.Label(new Rect(12, 36, W, 20),
                "WASD 移动　E 交互　F 开火　G 自动菜谱　Q 切换手持　1/2/3 菜园/厨房/饭厅　B 建造　K 菜谱　H 招募　P 商店　N 联机　空格 暂停　T 加速", small);
        }

        void Toasts()
        {
            float y = 62;
            foreach (var t in G.toasts.Skip(Mathf.Max(0, G.toasts.Count - 5)))
            {
                var size = toast.CalcSize(new GUIContent(t.text));
                GUI.Label(new Rect(W / 2 - size.x / 2 - 12, y, size.x + 24, 28), t.text, toast);
                y += 30;
            }
        }

        void Prompt()
        {
            var t = G.player.target;
            if (t == null) return;
            var p = t.Prompt(G.player);
            if (string.IsNullOrEmpty(p)) return;
            var size = toast.CalcSize(new GUIContent(p));
            GUI.Label(new Rect(W / 2 - size.x / 2 - 12, H - 120, size.x + 24, 30), p, toast);
        }

        void BagBar()
        {
            var bag = G.player.bag;
            int n = bag.slots.Count + 1;
            float sw = 70, sh = 50, total = n * (sw + 4);
            float x = W / 2 - total / 2, y = H - 80;
            GUI.Box(UI(new Rect(x - 6, y - 6, total + 8, sh + 12)), GUIContent.none, box);
            if (GUI.Button(new Rect(x, y, sw, sh), "空手", G.player.selected < 0 ? slotSel : slot)) G.player.selected = -1;
            for (int i = 0; i < bag.slots.Count; i++)
            {
                var s = bag.slots[i];
                var r = new Rect(x + (i + 1) * (sw + 4), y, sw, sh);
                if (GUI.Button(r, $"{s.item.Name}\n×{s.count}", G.player.selected == i ? slotSel : slot)) G.player.selected = i;
                GUI.color = s.item.def.color;
                GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, 8, 8), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        void WorldLabels()
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var b in G.buildings)
            {
                var t = b.Label();
                if (t != null) WorldText(cam, b.Center + Vector3.up * (b.def.height + 0.5f), t, small, Color.white);
            }
            if (G.players.Count > 1)
                foreach (var p in G.players)
                    WorldText(cam, p.transform.position + Vector3.up * 1.75f, p.playerName + (p.isLocal ? "（你）" : ""), label, Color.Lerp(Player.SlotColors[p.slot % Player.SlotColors.Length], Color.white, 0.45f));
            foreach (var s in G.staff) WorldText(cam, s.transform.position + Vector3.up * 1.9f, s.Label(), small, new Color(0.7f, 1f, 0.7f));
            foreach (var c in G.customers)
            {
                var t = c.Bubble();
                if (t == null) continue;
                var pos = cam.WorldToScreenPoint(c.transform.position + Vector3.up * 1.9f);
                if (pos.z < 0) continue;
                var size = bubble.CalcSize(new GUIContent(t));
                var r = new Rect(pos.x / scale - size.x / 2, (Screen.height - pos.y) / scale - size.y, size.x, size.y);
                var old = bubble.normal.textColor;
                if (c.state == Customer.State.Waiting && c.waited > c.def.patience) bubble.normal.textColor = new Color(0.8f, 0.1f, 0.1f);
                GUI.Label(r, t, bubble);
                bubble.normal.textColor = old;
            }
            var tgt = G.player.target;
            if (tgt != null) WorldText(cam, tgt.Center + Vector3.up * 0.2f, "▼", title, new Color(1f, 0.85f, 0.3f));
        }

        void WorldText(Camera cam, Vector3 world, string text, GUIStyle style, Color color)
        {
            var pos = cam.WorldToScreenPoint(world);
            if (pos.z < 0) return;
            var size = style.CalcSize(new GUIContent(text));
            var r = new Rect(pos.x / scale - size.x / 2, (Screen.height - pos.y) / scale - size.y, size.x, size.y);
            var old = style.normal.textColor;
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        // ───────── 建造栏 ─────────
        static readonly (string group, BuildKind[] kinds)[] Groups =
        {
            ("菜园", new[] { BuildKind.Plot, BuildKind.Coop, BuildKind.Sprinkler, BuildKind.Harvester, BuildKind.Seeder }),
            ("厨房", new[] { BuildKind.Stove, BuildKind.PassWindow }),
            ("饭厅", new[] { BuildKind.Table, BuildKind.Chair, BuildKind.Decor, BuildKind.Counter }),
            ("物流", new[] { BuildKind.Belt, BuildKind.Inserter, BuildKind.Container }),
        };

        void BuildPalette()
        {
            var bc = G.build;
            var area = UI(new Rect(8, 62, 250, H - 70));
            GUI.Box(area, GUIContent.none, box);
            GUILayout.BeginArea(new Rect(area.x + 6, area.y + 6, area.width - 12, area.height - 12));
            GUILayout.Label("建造（B 退出）", title);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var (group, kinds) in Groups)
            {
                GUILayout.Label(group, label);
                foreach (var d in G.db.buildables.Where(b => kinds.Contains(b.kind)))
                {
                    string txt = $"{d.displayName}　<color=#ffd75e>{d.cost}</color>";
                    if (GUILayout.Toggle(bc.selected == d, txt, button) && bc.selected != d) bc.Select(d);
                }
            }
            GUILayout.EndScrollView();
            if (GUILayout.Toggle(bc.demolish, "拆除模式（X）返还 100%", button) != bc.demolish) { bc.demolish = !bc.demolish; if (bc.demolish) bc.selected = null; }
            GUILayout.EndArea();

            string info = bc.demolish ? "点击建筑拆除（里面的东西回到背包）"
                : bc.selected == null ? "从左侧选择要建造的东西"
                : $"{bc.selected.displayName}：{bc.selected.blurb}　R 旋转　右键取消" + (bc.hoverValid ? "" : $"　<color=#ff8080>{bc.hoverReason}</color>");
            var size = toast.CalcSize(new GUIContent(info));
            GUI.Label(new Rect(W / 2 - size.x / 2, H - 60, size.x + 24, 30), info, toast);
        }

        // ───────── 面板 ─────────
        Rect Window(string caption, float w = 760, float h = 560)
        {
            var r = UI(new Rect(W / 2 - w / 2, H / 2 - h / 2, w, h));
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(new Rect(r.x + 14, r.y + 8, w, 30), caption, title);
            if (GUI.Button(new Rect(r.xMax - 40, r.y + 8, 30, 26), "×", button)) G.panel = InnGame.Panel.None;
            return new Rect(r.x + 14, r.y + 44, w - 28, h - 56);
        }

        void RecipePanel()
        {
            var r = Window($"菜谱（已发现 {G.discovered.Count}/{G.db.recipes.Length}）— 只有 Active 的菜会被客人点");
            GUILayout.BeginArea(r);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var rec in G.db.recipes)
            {
                GUILayout.BeginHorizontal();
                if (G.discovered.Contains(rec))
                {
                    var o = rec.output;
                    string kind = o.IsDish ? $"<color=#ffd75e>{o.price} 金</color>" : "中间食材";
                    GUILayout.Label($"<b>{o.displayName}</b>　{rec.Describe()}　{rec.seconds:0} 秒　{kind}", label, GUILayout.Width(560));
                    if (o.IsDish)
                    {
                        bool on = !G.disabledDishes.Contains(o);
                        bool now = GUILayout.Toggle(on, on ? "<color=#7dff8a>Active 在售</color>" : "<color=#ff8a7d>Inactive 停售</color>", button, GUILayout.Width(140));
                        if (now != on) G.net.ToggleDish(o, now);
                    }
                }
                else
                {
                    // 提示：只告诉烹饪方式和食材数量
                    GUILayout.Label($"<color=#999999>？？？　{rec.ingredients.Length} 种食材（{Names.Of(rec.method)}）</color>", label);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void StaffPanel()
        {
            var r = Window($"招募榜 / 员工（{G.staff.Count}/{G.StaffCap}）");
            GUILayout.BeginArea(r);
            GUILayout.Label("今日候选（每天刷新）", label);
            for (int ri = 0; ri < G.recruits.Count; ri++)
            {
                GUILayout.BeginHorizontal();
                var d = G.recruits[ri];
                GUILayout.Label($"{d.Summary}　擅长 {StaffData.AttrName(d.main)}", label, GUILayout.Width(500));
                foreach (var job in new[] { StaffJob.Chef, StaffJob.Waiter, StaffJob.Gardener })
                    if (GUILayout.Button("雇为" + Names.Of(job), button, GUILayout.Width(70))) { G.net.Hire(ri, job); GUIUtility.ExitGUI(); }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button($"花 {G.db.recruitRefreshCost} 金刷新候选", button, GUILayout.Width(220))) G.net.RefreshRecruits();
            GUILayout.Space(14);
            GUILayout.Label("已雇员工", label);
            foreach (var s in G.staff.ToList())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{s.data.Summary}　[{Names.Of(s.job)}] {s.status}", label, GUILayout.Width(560));
                if (GUILayout.Button("换岗", button, GUILayout.Width(60)))
                    G.net.SetJob(s, s.job == StaffJob.Chef ? StaffJob.Waiter : s.job == StaffJob.Waiter ? StaffJob.Gardener : StaffJob.Chef);
                if (GUILayout.Button("解雇", button, GUILayout.Width(60))) { G.net.Fire(s); GUIUtility.ExitGUI(); }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        void ShopPanel()
        {
            var r = Window("种子商店");
            GUILayout.BeginArea(r);
            foreach (var c in G.db.crops)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{c.seed.displayName}　{c.seedPrice} 金/个　生长 {c.growSeconds:0} 秒　产出 {c.yield}　{(c.needsWater ? "需浇水" : "不用浇水")}", label, GUILayout.Width(520));
                if (GUILayout.Button("买 5 个", button, GUILayout.Width(90))) G.net.BuySeed(c, 5);
                if (GUILayout.Button("买 20 个", button, GUILayout.Width(90))) G.net.BuySeed(c, 20);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        string ipField = "127.0.0.1", nameField, portField = Net.LanTransport.DefaultPort.ToString();

        void NetPanel()
        {
            var net = G.net;
            var r = Window($"联机（合作经营，最多 {Net.NetProto.MaxPlayers} 人）— {net.Status}", 760, 520);
            GUILayout.BeginArea(r);
            if (nameField == null) nameField = net.LocalName;
            GUILayout.BeginHorizontal();
            GUILayout.Label("你的名字", label, GUILayout.Width(80));
            nameField = GUILayout.TextField(nameField, 12, GUILayout.Width(180));
            if (GUILayout.Button("确定", button, GUILayout.Width(60))) { net.SetName(nameField); GUI.FocusControl(null); }
            GUILayout.EndHorizontal();
            int.TryParse(portField, out int port);
            if (port <= 0) port = Net.LanTransport.DefaultPort;
            GUILayout.Space(8);

            if (!net.Online)
            {
                GUILayout.Label("<b>开房间</b>：用你现在的客栈当主机，朋友加入后一起经营（金币、名望、建筑、员工共享，背包各自的）", label);
                GUILayout.BeginHorizontal();
                GUILayout.Label("端口", label, GUILayout.Width(40));
                portField = GUILayout.TextField(portField, 5, GUILayout.Width(70));
                if (GUILayout.Button("开房间", button, GUILayout.Width(120))) { net.SetName(nameField); net.Host(port); GUI.FocusControl(null); }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
                GUILayout.Label("<b>加入</b>：同一局域网的房间会自动出现在下面；外网填主机的公网 IP（主机要做端口映射）或虚拟局域网 IP", label);
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP", label, GUILayout.Width(40));
                ipField = GUILayout.TextField(ipField, 40, GUILayout.Width(200));
                if (GUILayout.Button("加入", button, GUILayout.Width(120))) { net.SetName(nameField); GUI.FocusControl(null); net.Join(ipField.Trim(), port); GUIUtility.ExitGUI(); }
                GUILayout.EndHorizontal();
                GUILayout.Label("局域网里的房间：", label);
                var hosts = net.FoundHosts();
                if (hosts.Count == 0) GUILayout.Label("<color=#999999>（暂时没找到，主机开房间后 1~2 秒会出现）</color>", label);
                foreach (var h in hosts)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{h.Name}　{h.Ip}:{h.Port}　{h.Players}/{h.Max} 人", label, GUILayout.Width(460));
                    if (GUILayout.Button(h.Players >= h.Max ? "已满" : "加入", button, GUILayout.Width(100)) && h.Players < h.Max)
                    { net.SetName(nameField); GUI.FocusControl(null); net.Join(h.Ip, h.Port); GUIUtility.ExitGUI(); }
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label("提示：第一次开房间时 Windows 防火墙会询问，请允许（勾选“专用网络”）。", small);
            }
            else
            {
                if (net.IsHost) GUILayout.Label($"房间地址（发给朋友）：<b>{net.transport.Address}</b>", label);
                GUILayout.Label("在客栈里的掌柜：", label);
                foreach (var p in G.players)
                    GUILayout.Label($"　● {p.playerName}{(p.isLocal ? "（你）" : "")}{(p.netId == 1 ? "　[主机]" : "")}", label);
                GUILayout.Space(10);
                if (GUILayout.Button(net.IsHost ? "关闭房间（继续单机）" : "离开（回到自己的单机客栈）", button, GUILayout.Width(300))) { net.Leave(); GUIUtility.ExitGUI(); }
                GUILayout.Label("说明：客栈由主机运行，主机退出后大家都会断开；原型还没有存档，退出后进度不保留。", small);
            }
            GUILayout.EndArea();
        }

        void Summary()
        {
            var s = G.summary;
            var r = UI(new Rect(W / 2 - 240, H / 2 - 170, 480, 340));
            GUI.Box(r, GUIContent.none, box);
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, r.width - 40, r.height - 28));
            GUILayout.Label($"第 {s.day} 天 结算", title);
            GUILayout.Label($"菜钱　　<color=#ffd75e>+{s.income}</color>", label);
            GUILayout.Label($"小费　　<color=#ffd75e>+{s.tips}</color>", label);
            GUILayout.Label($"工钱　　<color=#ff8080>-{s.wages}</color>", label);
            GUILayout.Label($"维护费　<color=#ff8080>-{s.upkeep}</color>", label);
            GUILayout.Label($"净收入　{s.income + s.tips - s.wages - s.upkeep}", title);
            GUILayout.Label($"接待 {s.served} 位　流失 {s.left} 位　名望 {s.rep:+0;-0;0}", label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("开始新的一天（Enter）", button, GUILayout.Height(36))) G.net.NextDay();
            GUILayout.EndArea();
        }
    }
}
