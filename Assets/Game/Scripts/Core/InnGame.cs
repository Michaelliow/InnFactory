using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Inn
{
    public class InnStats
    {
        public int harvested, cooked, dishesServed, beltMoves, customersPaid, customersLeft, discovered;
        public int income, tips, repGained;
    }

    public class DaySummary
    {
        public int day, income, tips, wages, upkeep, served, left, rep;
    }

    /// <summary>
    /// 客栈总控：世界生成、经济、时间、菜谱发现、顾客刷新、员工、建造。全部数值来自 GameDatabase。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class InnGame : MonoBehaviour
    {
        public static InnGame I;

        public GameDatabase db;
        public GridMap grid;
        public Player player;
        public BuildController build;
        public InnStats Stats = new InnStats();

        public readonly List<Building> buildings = new List<Building>();
        public readonly List<Plot> plots = new List<Plot>();
        public readonly List<Stove> stoves = new List<Stove>();
        public readonly List<Container> containers = new List<Container>();
        public readonly List<PassWindow> passWindows = new List<PassWindow>();
        public readonly List<Chair> chairs = new List<Chair>();
        public readonly List<Customer> customers = new List<Customer>();
        public readonly List<Staff> staff = new List<Staff>();
        public readonly List<StaffData> recruits = new List<StaffData>();

        public int gold;
        public int rep;
        public int day;
        public float clock;          // 当天已过秒数
        public bool Open = true;
        public DaySummary summary;   // 非空 = 显示结算面板
        DaySummary today = new DaySummary();

        public readonly HashSet<RecipeDef> discovered = new HashSet<RecipeDef>();
        public readonly HashSet<ItemDef> disabledDishes = new HashSet<ItemDef>();
        readonly Dictionary<string, RecipeDef> recipeByKey = new Dictionary<string, RecipeDef>();

        public readonly List<(string text, float until)> toasts = new List<(string, float)>();
        public int speedIndex;
        public static readonly float[] Speeds = { 1, 2, 4 };
        public bool userPaused;
        public float spawnTimer = 5;
        public ItemDef forcedOrder;      // 自动测试用：强制顾客点这道菜
        public bool spawnEnabled = true; // 自动测试用
        public float timeScaleOverride;  // 自动测试用（>0 时覆盖游戏速度）
        readonly System.Random rng = new System.Random();

        public enum Panel { None, Recipes, Staff, Shop, Net }
        public Panel panel;

        public bool Paused => userPaused || summary != null;
        public bool ModalOpen => summary != null;

        // ── 联机 ──
        public Net.InnNet net;
        public bool Replica;                         // 联机客户端：不模拟，只显示主机快照
        public readonly List<Player> players = new List<Player>();
        int nextNetId = 100;
        public int NewNetId() => nextNetId++;
        public Player toastTarget;                   // 主机执行某个掌柜的操作时，提示只发给他
        public float replicaTimeScale = 1;

        void Awake()
        {
            I = this;
            db = GameDatabase.Load();
            Vis.Init(db.baseMaterial);
            grid = new GridMap(db);
            foreach (var r in db.recipes) recipeByKey[r.Key()] = r;
            foreach (var r in db.recipes) if (r.startKnown) discovered.Add(r);
            gold = db.startGold;
            build = new BuildController(this);

            BuildWorld();
            foreach (var e in db.startLayout) Place(e.def, e.cell, e.dir, false);

            var pgo = new GameObject("Player");
            player = pgo.AddComponent<Player>();
            pgo.transform.position = ZoneSpawn(Zone.Kitchen);
            foreach (var s in db.startBag) player.bag.Add(new Item(s.item), s.count);
            players.Add(player);

            day = 1;
            RefreshRecruits(false);
            net = gameObject.AddComponent<Net.InnNet>();
            player.playerName = net.LocalName;
        }

        void Update()
        {
            InnInput.BeginFrame();
            HandleKeys();
            Time.timeScale = Replica ? replicaTimeScale : Paused ? 0 : timeScaleOverride > 0 ? timeScaleOverride : Speeds[speedIndex];
            build.Update();
            toasts.RemoveAll(t => t.until < Time.unscaledTime);
            if (Paused || Replica) return;

            clock += Time.deltaTime;
            if (Open && clock >= db.dayLengthSeconds)
            {
                Open = false;
                Toast("打烊了！等客人都走后结算今天的账");
            }
            if (!Open && customers.Count == 0 && summary == null) Settle();

            if (Open && spawnEnabled)
            {
                spawnTimer -= Time.deltaTime;
                if (spawnTimer <= 0)
                {
                    spawnTimer = db.arrivalBase / (1 + RepLevel * db.arrivalPerLevel) * Random.Range(0.7f, 1.3f);
                    if (MenuDishes().Any() && customers.Count < chairs.Count(c => c.IsSeat) + 3) SpawnCustomer();
                }
            }
        }

        void HandleKeys()
        {
            if (InnInput.Typing) return;   // 正在输入名字 / IP 时不触发快捷键
            if (summary != null)
            {
                if (InnInput.Down(KeyCode.Return) || InnInput.Down(KeyCode.KeypadEnter)) net.NextDay();
                return;
            }
            if (InnInput.Down(KeyCode.Space)) net.Pause();
            if (InnInput.Down(KeyCode.T)) net.Speed();
            if (InnInput.Down(KeyCode.B)) { build.Toggle(); panel = Panel.None; }
            if (InnInput.Down(KeyCode.K)) TogglePanel(Panel.Recipes);
            if (InnInput.Down(KeyCode.H)) TogglePanel(Panel.Staff);
            if (InnInput.Down(KeyCode.P)) TogglePanel(Panel.Shop);
            if (InnInput.Down(KeyCode.N)) TogglePanel(Panel.Net);
            if (InnInput.Down(KeyCode.Escape)) { panel = Panel.None; if (build.active) build.Toggle(); }
        }

        void TogglePanel(Panel p) { panel = panel == p ? Panel.None : p; if (build.active) build.Toggle(); }

        // ───────────────────────── 世界 ─────────────────────────
        void BuildWorld()
        {
            var root = new GameObject("World").transform;
            var zoneColor = new Dictionary<Zone, Color>
            {
                { Zone.Garden, new Color(0.45f, 0.66f, 0.32f) },
                { Zone.Kitchen, new Color(0.66f, 0.64f, 0.6f) },
                { Zone.Dining, new Color(0.78f, 0.6f, 0.42f) },
            };
            Vis.Box(root, new Vector3(grid.width / 2f, -0.08f, grid.height / 2f), new Vector3(grid.width + 12, 0.1f, grid.height + 12), new Color(0.4f, 0.52f, 0.3f), "Ground");
            for (int x = 0; x < grid.width; x++)
                for (int y = 0; y < grid.height; y++)
                {
                    var z = grid.ZoneOf(new Vector2Int(x, y));
                    var c = zoneColor[z] * ((x + y) % 2 == 0 ? 1f : 0.94f); c.a = 1;
                    Vis.Box(root, new Vector3(x + 0.5f, -0.02f, y + 0.5f), new Vector3(0.97f, 0.04f, 0.97f), c);
                }

            var wall = new Color(0.55f, 0.38f, 0.25f);
            var wallTop = new Color(0.35f, 0.3f, 0.3f);
            // 远侧（北、东）墙高，近侧和区间墙矮，避免挡住等距视角
            for (int x = 0; x < grid.width; x++)
            {
                bool garden = x < grid.kitchenX;
                float h = garden ? 0.4f : 1.6f;
                Vis.Box(root, new Vector3(x + 0.5f, h / 2, grid.height + 0.1f), new Vector3(1f, h, 0.2f), garden ? new Color(0.6f, 0.45f, 0.3f) : wall);
                if (!garden) Vis.Box(root, new Vector3(x + 0.5f, h + 0.05f, grid.height + 0.1f), new Vector3(1.02f, 0.1f, 0.3f), wallTop);
                Vis.Box(root, new Vector3(x + 0.5f, 0.15f, -0.1f), new Vector3(1f, 0.3f, 0.2f), garden ? new Color(0.6f, 0.45f, 0.3f) : wall);
            }
            for (int y = 0; y < grid.height; y++)
            {
                bool door = y >= db.doorRows.x && y <= db.doorRows.y;
                if (!door)
                {
                    Vis.Box(root, new Vector3(grid.kitchenX, 0.3f, y + 0.5f), new Vector3(0.15f, 0.6f, 1f), wall);
                    Vis.Box(root, new Vector3(grid.diningX, 0.3f, y + 0.5f), new Vector3(0.15f, 0.6f, 1f), wall);
                }
                bool entrance = y == EntranceRow || y == EntranceRow - 1;
                if (!entrance)
                {
                    Vis.Box(root, new Vector3(grid.width + 0.1f, 0.8f, y + 0.5f), new Vector3(0.2f, 1.6f, 1f), wall);
                    Vis.Box(root, new Vector3(grid.width + 0.1f, 1.65f, y + 0.5f), new Vector3(0.3f, 0.1f, 1.02f), wallTop);
                }
                Vis.Box(root, new Vector3(-0.1f, 0.2f, y + 0.5f), new Vector3(0.2f, 0.4f, 1f), new Color(0.6f, 0.45f, 0.3f));
            }
            // 门口灯笼 + 招牌
            Vis.Box(root, new Vector3(grid.width + 0.3f, 2.1f, EntranceRow), new Vector3(0.12f, 0.5f, 2.4f), new Color(0.45f, 0.15f, 0.1f), "Signboard");
        }

        public const int EntranceRow = 4;
        public Vector3 DoorInside => new Vector3(grid.width - 0.7f, 0, EntranceRow);
        public Vector3 DoorOutside => new Vector3(grid.width + 3f, 0, EntranceRow);

        public Vector3 ZoneSpawn(Zone z) => z switch
        {
            Zone.Garden => new Vector3(6.5f, 0, 3.5f),
            Zone.Kitchen => new Vector3(16.5f, 0, 4.5f),
            _ => new Vector3(24.5f, 0, 2.5f),
        };

        // ───────────────────────── 建造 ─────────────────────────
        public Building Place(BuildableDef def, Vector2Int cell, int dir, bool pay, int netId = 0)
        {
            string reason = "没有选择建筑";
            if (def == null || !grid.CanPlace(def, cell, dir, out reason)) { if (pay) Toast("不能放置：" + reason); return null; }
            if (pay && gold < def.cost) { Toast($"金币不够（需要 {def.cost}）"); return null; }
            var go = new GameObject(def.displayName);
            go.transform.SetParent(transform, false);
            Building b = def.kind switch
            {
                BuildKind.Plot => go.AddComponent<Plot>(),
                BuildKind.Coop => go.AddComponent<Producer>(),
                BuildKind.Container => go.AddComponent<Container>(),
                BuildKind.Stove => go.AddComponent<Stove>(),
                BuildKind.PassWindow => go.AddComponent<PassWindow>(),
                BuildKind.Chair => go.AddComponent<Chair>(),
                BuildKind.Belt => go.AddComponent<Belt>(),
                BuildKind.Inserter => go.AddComponent<Inserter>(),
                BuildKind.Sprinkler => go.AddComponent<Sprinkler>(),
                BuildKind.Harvester => go.AddComponent<Harvester>(),
                BuildKind.Seeder => go.AddComponent<Seeder>(),
                _ => go.AddComponent<Furniture>(),
            };
            b.netId = netId != 0 ? netId : NewNetId();
            b.Init(def, cell, dir);
            grid.Occupy(b, true);
            buildings.Add(b);
            switch (b)
            {
                case Plot p: plots.Add(p); break;
                case Stove s: stoves.Add(s); break;
                case Container c: containers.Add(c); break;
                case PassWindow w: passWindows.Add(w); break;
                case Chair ch: chairs.Add(ch); break;
            }
            if (pay) gold -= def.cost;
            return b;
        }

        /// <summary>拆除：返还造价，里面的东西进 who（拆的人）的背包</summary>
        public void Remove(Building b, Player who = null)
        {
            if (b == null) return;
            if (b is Chair ch && (ch.occupant != null || ch.reserved != null)) { Toast("有客人坐着，不能拆"); return; }
            if (b is Stove st && (st.cooking || st.reservedBy != null)) { Toast("灶台正在使用，不能拆"); return; }
            b.OnRemoved((who ?? player).bag);
            gold += b.def.cost;
            Unregister(b);
        }

        void Unregister(Building b)
        {
            grid.Occupy(b, false);
            buildings.Remove(b);
            plots.Remove(b as Plot); stoves.Remove(b as Stove); containers.Remove(b as Container);
            passWindows.Remove(b as PassWindow); chairs.Remove(b as Chair);
            Destroy(b.gameObject);
        }

        /// <summary>客户端：按快照删除（不返还）</summary>
        public void RemoveReplica(Building b) => Unregister(b);

        /// <summary>加入别人的房间：清空本地世界，之后全部按主机快照重建</summary>
        public void BecomeReplica()
        {
            Replica = true;
            foreach (var b in buildings.ToList()) Unregister(b);
            foreach (var c in customers.ToList()) if (c) Destroy(c.gameObject);
            customers.Clear();
            foreach (var s in staff.ToList()) if (s) Destroy(s.gameObject);
            staff.Clear();
            foreach (var p in players.ToList()) if (p != player) Destroy(p.gameObject);
            players.Clear(); players.Add(player);
            player.bag.Clear();
            summary = null; panel = Panel.None;
            if (build.active) build.Toggle();
        }

        public int BuildValue => buildings.Sum(b => b.def.cost);

        // ───────────────────────── 菜谱 ─────────────────────────
        public RecipeDef FindRecipe(CookMethod m, IEnumerable<ItemDef> items)
        {
            var list = items.Where(i => i != null).ToList();
            if (list.Count == 0) return null;
            return recipeByKey.TryGetValue(RecipeDef.Key(m, list), out var r) ? r : null;
        }

        public void Discover(RecipeDef r)
        {
            if (r == null || !discovered.Add(r)) return;
            Stats.discovered++;
            Toast($"发现新菜谱：{r.output.displayName}！（{r.Describe()}）", 5);
        }

        public IEnumerable<RecipeDef> KnownRecipes() => db.recipes.Where(r => discovered.Contains(r));

        public IEnumerable<ItemDef> MenuDishes() =>
            KnownRecipes().Select(r => r.output).Where(o => o.IsDish && !disabledDishes.Contains(o)).Distinct();

        public RecipeDef KnownRecipeFor(ItemDef output) => KnownRecipes().FirstOrDefault(r => r.output == output);

        // ───────────────────────── 库存查询 ─────────────────────────
        public int AvailableCount(ItemDef d) => containers.Sum(c => c.bag.Count(d));

        public Container FindContainerWith(ItemDef d, Vector3 near) =>
            containers.Where(c => c.bag.Count(d) > 0).OrderBy(c => c.DistanceTo(near)).FirstOrDefault();

        public Container NearestContainer(Vector3 near, Item it) =>
            containers.Where(c => c.bag.CanAdd(it)).OrderBy(c => c.DistanceTo(near)).FirstOrDefault();

        /// <summary>还需要做多少份：顾客未上的菜 − 出菜口已有 − 跑堂在送 − 厨师在做</summary>
        public Dictionary<ItemDef, int> Demand()
        {
            var d = new Dictionary<ItemDef, int>();
            foreach (var c in customers)
                if (c.state == Customer.State.Waiting)
                    foreach (var o in c.orders.Distinct())
                        d[o] = (d.TryGetValue(o, out var n) ? n : 0) + c.Outstanding(o);
            foreach (var p in passWindows)
                foreach (var s in p.bag.slots)
                    if (d.ContainsKey(s.item.def)) d[s.item.def] -= s.count;
            foreach (var s in staff)
                if (s.cookingFor != null && d.ContainsKey(s.cookingFor)) d[s.cookingFor]--;
            return d;
        }

        /// <summary>厨师下一步做什么：挑一道有需求的菜，缺中间食材就先做中间食材；要有空闲的对应灶台</summary>
        public RecipeDef PlanChefStep(out Stove stove, out ItemDef dish)
        {
            stove = null;
            dish = null;
            var demand = Demand().Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            foreach (var d in demand)
            {
                dish = d;
                var step = Step(d, 0);
                if (step == null) continue;
                stove = stoves.Where(s => s.Method == step.method && s.Free).OrderBy(s => s.DistanceTo(ZoneSpawn(Zone.Kitchen))).FirstOrDefault();
                if (stove != null) return step;
            }
            return null;
        }

        RecipeDef Step(ItemDef target, int depth)
        {
            var r = KnownRecipeFor(target);
            if (r == null || depth > 3) return null;
            foreach (var ing in r.ingredients.Distinct())
                if (AvailableCount(ing) < r.CountOf(ing)) return Step(ing, depth + 1);
            return r;
        }

        // ───────────────────────── 顾客 ─────────────────────────
        public Chair FindSeat() => chairs.Where(c => c.Vacant).OrderBy(c => Vector3.Distance(c.Center, DoorInside)).FirstOrDefault();

        public Building Counter => buildings.FirstOrDefault(b => b.def.kind == BuildKind.Counter);

        public int DecorBonus
        {
            get
            {
                var r = grid.ZoneRect(Zone.Dining);
                int sum = buildings.Where(b => b.Zone == Zone.Dining).Sum(b => b.def.decor);
                return Mathf.Min(db.decorCap, Mathf.RoundToInt(sum * 100f / (r.width * r.height)));
            }
        }

        public Customer SpawnCustomer(CustomerDef def = null)
        {
            if (def == null)
            {
                var pool = db.customers.Where(c => c.minRepLevel <= RepLevel).ToList();
                float total = pool.Sum(c => c.weight), r = Random.value * total;
                def = pool[pool.Count - 1];
                foreach (var c in pool) { r -= c.weight; if (r <= 0) { def = c; break; } }
            }
            var go = new GameObject("Customer");
            go.transform.position = DoorOutside;
            var cu = go.AddComponent<Customer>();
            cu.netId = NewNetId();
            customers.Add(cu);
            cu.Setup(def);
            return cu;
        }

        public void CustomerLeft(Customer c, int pay, int tip, int repDelta, string reason)
        {
            gold += pay + tip;
            rep = Mathf.Max(0, rep + repDelta);
            today.income += pay; today.tips += tip; today.rep += repDelta;
            Stats.income += pay; Stats.tips += tip; Stats.repGained += repDelta;
            if (pay > 0)
            {
                today.served++; Stats.customersPaid++;
                Toast($"{c.def.displayName}结账 {pay} 金" + (tip > 0 ? $" + 小费 {tip}" : "") + $"（满意度 {c.satisfaction:0}，名望 {repDelta:+0;-0}）");
            }
            else
            {
                today.left++; Stats.customersLeft++;
                Toast($"{c.def.displayName}离开了：{reason}" + (repDelta < 0 ? $"（名望 {repDelta}）" : ""));
            }
        }

        // ───────────────────────── 名望 ─────────────────────────
        public int RepLevel
        {
            get
            {
                int lv = 1;
                for (int i = 0; i < db.repThresholds.Length; i++) if (rep >= db.repThresholds[i]) lv = i + 1;
                return lv;
            }
        }

        public int NextRepThreshold => RepLevel < db.repThresholds.Length ? db.repThresholds[RepLevel] : rep;

        // ───────────────────────── 员工 ─────────────────────────
        public int StaffCap => db.staffBaseCap + RepLevel;

        public void RefreshRecruits(bool pay)
        {
            if (pay)
            {
                if (gold < db.recruitRefreshCost) { Toast("金币不够刷新"); return; }
                gold -= db.recruitRefreshCost;
            }
            recruits.Clear();
            for (int i = 0; i < db.recruitCount; i++) recruits.Add(StaffData.Roll(db, rng));
        }

        public Staff Hire(StaffData d, StaffJob job)
        {
            if (staff.Count >= StaffCap) { Toast($"员工已满（上限 {StaffCap} = {db.staffBaseCap} + 名望等级）"); return null; }
            recruits.Remove(d);
            var go = new GameObject("Staff");
            go.transform.position = ZoneSpawn(Zone.Kitchen) + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
            var s = go.AddComponent<Staff>();
            s.netId = NewNetId();
            staff.Add(s);
            s.Setup(d, job);
            Toast($"雇佣了{Names.Of(job)} {d.name}{Names.Stars(d.stars)}（日薪 {d.wage}）");
            return s;
        }

        public void Fire(Staff s)
        {
            staff.Remove(s);
            s.SetJob(StaffJob.None);
            Destroy(s.gameObject);
        }

        // ───────────────────────── 商店 ─────────────────────────
        /// <summary>买种子：钱从公共金库出，种子进买的人背包</summary>
        public bool BuySeed(CropDef c, int n, Player who = null)
        {
            int cost = c.seedPrice * n;
            if (gold < cost) { Toast("金币不够"); return false; }
            gold -= cost;
            (who ?? player).bag.Add(new Item(c.seed), n);
            return true;
        }

        // ───────────────────────── 一天 ─────────────────────────
        public string ClockText
        {
            get
            {
                float h = 6 + 16 * Mathf.Clamp01(clock / db.dayLengthSeconds);
                return $"{(int)h:00}:{(int)((h % 1) * 60):00}";
            }
        }

        public void Settle()
        {
            today.day = day;
            today.wages = staff.Sum(s => s.data.wage);
            today.upkeep = db.upkeepBase + Mathf.RoundToInt(BuildValue * db.upkeepPerBuildValue);
            gold -= today.wages + today.upkeep;
            summary = today;
        }

        public void EndDayNow() { clock = db.dayLengthSeconds; }

        public void NextDay()
        {
            summary = null;
            today = new DaySummary();
            day++;
            clock = 0;
            Open = true;
            spawnTimer = 3;
            RefreshRecruits(false);
            Toast($"第 {day} 天开张！");
        }

        /// <summary>
        /// 游戏提示。主机上：默认发给所有人；在执行某个掌柜的操作时（toastTarget）只给他。客户端上只显示在本机。
        /// </summary>
        public void Toast(string text, float seconds = 3)
        {
            if (!Replica && net != null && net.IsHost)
            {
                if (toastTarget != null && !toastTarget.isLocal) { net.SendToast(toastTarget, text); return; }
                if (toastTarget == null) net.BroadcastToast(text);
            }
            LocalToast(text, seconds);
        }

        public void LocalToast(string text, float seconds = 3) => toasts.Add((text, Time.unscaledTime + seconds));
    }
}
