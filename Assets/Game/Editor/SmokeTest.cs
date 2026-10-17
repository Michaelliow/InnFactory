using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Inn.EditorTools
{
    /// <summary>
    /// Play 模式自动测试：模拟按键跑通 种菜 → 收获 → 合成菜谱（含发现 / 黑暗料理）→ 上菜 → 收钱 的闭环，
    /// 以及建造、传送带 + 机关手 + 洒水器 + 收割 / 播种机关、员工（厨师 / 跑堂 / 园丁）、每日结算。
    /// 结果写入 Logs/smoke.txt，截图 Logs/shots/play_*.png。
    /// 命令行：Unity -batchmode -projectPath . -executeMethod Inn.EditorTools.SmokeTest.BuildAndRun
    /// </summary>
    [InitializeOnLoad]
    public static class SmokeTest
    {
        const string Flag = "INN_SMOKE_RUNNING";

        static SmokeTest()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
                    new GameObject("SmokeRunner").AddComponent<SmokeRunner>();
            };
        }

        [MenuItem("InnFactory/Debug/Run Play-Mode Smoke Test")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(InnSceneFactory.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(Flag, true);
            EditorApplication.EnterPlaymode();
        }

        public static void BuildAndRun()
        {
            InnSetup.RunAll();
            // 联机测试要用打包后的游戏当客户端
            SessionState.SetBool("INN_PLAYER_BUILT", InnSetup.BuildPlayer());
            Run();
        }

        public static void Finish(string report, int failures)
        {
            SessionState.SetBool(Flag, false);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/smoke.txt", report);
            Debug.Log("[Smoke]\n" + report);
            if (Application.isBatchMode) EditorApplication.Exit(failures > 0 ? 1 : 0);
            else EditorApplication.ExitPlaymode();
        }
    }

    public class SmokeRunner : MonoBehaviour
    {
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> errors = new List<string>();
        int failures;
        float startedAt;
        bool finished;
        InnGame G;
        Player P;

        void Awake()
        {
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    if (!msg.StartsWith("[Smoke]") && !stack.Contains("UnityEditor.Search")) errors.Add(msg + "\n" + stack.Split('\n').FirstOrDefault());
            };
        }

        void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        void Update()
        {
            if (!finished && startedAt > 0 && Time.realtimeSinceStartup - startedAt > 600)
            {
                finished = true;
                StopAllCoroutines();
                log.Insert(0, "RESULT: TIMEOUT\n");
                SmokeTest.Finish(log.ToString(), failures + 1);
            }
            // 测试期间不让一天结束（结算测试里手动结束）
            if (G != null && keepDayOpen && G.Open) G.clock = Mathf.Min(G.clock, 60);
        }

        bool keepDayOpen = true;

        IEnumerator Start()
        {
            startedAt = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(1f);
            G = InnGame.I;
            P = G?.player;
            if (G == null || P == null)
            {
                Check(false, "场景里没有 InnGame / Player");
                Done();
                yield break;
            }
            G.spawnEnabled = false;

            IEnumerator[] steps =
            {
                DataTest(), MoveTest(), FarmTest(), CookTest(), ServeTest(), MenuToggleTest(), BuildTest(),
                AutomationTest(), StaffTest(), DayEndTest(), NetTest(),
            };
            foreach (var s in steps) yield return s;
            Done();
        }

        void Done()
        {
            int errCount = errors.Count;
            log.AppendLine($"== Console errors: {errCount}");
            foreach (var e in errors.Distinct().Take(20)) log.AppendLine("  " + e);
            if (errCount > 0) failures++;
            log.Insert(0, failures == 0 ? "RESULT: PASS\n" : $"RESULT: {failures} FAIL\n");
            Time.timeScale = 1;
            finished = true;
            SmokeTest.Finish(log.ToString(), failures);
        }

        // ───────────────────────── 工具 ─────────────────────────
        IEnumerator Frames(int n = 2) { for (int i = 0; i < n; i++) yield return null; }

        float Speed { set => G.timeScaleOverride = value; }

        /// <summary>站到建筑旁边一个能走的格子并面对它</summary>
        void StandAt(Building b)
        {
            var cands = new List<Vector2Int>();
            for (int x = b.cell.x - 1; x <= b.cell.x + b.Size.x; x++)
                for (int y = b.cell.y - 1; y <= b.cell.y + b.Size.y; y++)
                {
                    var c = new Vector2Int(x, y);
                    bool inside = x >= b.cell.x && x < b.cell.x + b.Size.x && y >= b.cell.y && y < b.cell.y + b.Size.y;
                    bool diagonal = (x < b.cell.x || x >= b.cell.x + b.Size.x) && (y < b.cell.y || y >= b.cell.y + b.Size.y);
                    if (inside || diagonal || !G.grid.InBounds(c)) continue;
                    var occ = G.grid.At(c);
                    if (occ != null && occ.def.solid) continue;
                    cands.Add(c);
                }
            // 优先站在没有其他建筑的格子
            var best = cands.OrderBy(c => G.grid.At(c) != null ? 1 : 0).ThenBy(c => c.y >= b.cell.y ? 1 : 0).First();
            var pos = GridMap.ToWorld(best);
            P.transform.position = pos;
            var face = b.ClosestPoint(pos) - pos; face.y = 0;
            P.transform.rotation = Quaternion.LookRotation(face.sqrMagnitude > 0.001f ? face : Vector3.forward);
        }

        /// <summary>走到建筑旁 → 确认被选为交互目标 → 模拟按键</summary>
        IEnumerator Press(Building b, KeyCode key, string what = null)
        {
            StandAt(b);
            yield return Frames(2);
            if (P.target != b) Check(false, $"{what ?? b.def.displayName}: 交互目标应为 {b.name}，实际 {(P.target ? P.target.name : "无")}");
            InnInput.Press(key);
            yield return Frames(2);
        }

        IEnumerator WaitUntil(Func<bool> cond, float realSeconds, Action tick = null)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond() && Time.realtimeSinceStartup - t0 < realSeconds) { tick?.Invoke(); yield return null; }
        }

        void Select(string id) => P.Select(G.db.Item(id));
        int Bag(string id) => P.bag.Count(G.db.Item(id));
        T Find<T>(string id) where T : Building => G.buildings.OfType<T>().First(b => b.def.id == id);
        BuildableDef Def(string id) => G.db.Buildable(id);

        void Shot(string name, Zone zone, float size = 6.5f) => Shot(name, G.grid.ZoneCenter(zone), size);

        void Shot(string name, Vector3 focus, float size)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var iso = cam.GetComponent<IsoCamera>();
            iso.Snap(focus, size);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;
            Directory.CreateDirectory("Logs/shots");
            File.WriteAllBytes($"Logs/shots/play_{name}.png", tex.EncodeToPNG());
            Destroy(tex);
            rt.Release();
            iso.overrideFocus = null;
        }

        // ───────────────────────── 1. 数据 ─────────────────────────
        IEnumerator DataTest()
        {
            log.AppendLine("== 数据");
            var problems = InnDataFactory.Validate(G.db);
            Check(problems.Count == 0, $"数据完整性（菜谱食材都有来源、无重复组合）：{problems.Count} 个问题 {string.Join("；", problems)}");
            Check(G.db.recipes.Length >= 16 && G.db.crops.Length == 8 && G.db.customers.Length == 5, $"内容数量：菜谱 {G.db.recipes.Length}、作物 {G.db.crops.Length}、顾客 {G.db.customers.Length}");
            Check(G.buildings.Count == G.db.startLayout.Length, $"初始布局 {G.buildings.Count}/{G.db.startLayout.Length} 个建筑放置成功");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Check(lit != null && !ShaderUtil.ShaderHasError(lit), "URP Lit 着色器");
            Check(G.chairs.Count(c => c.IsSeat) == 4, $"初始座位 {G.chairs.Count(c => c.IsSeat)}（应为 4）");
            Check(G.discovered.Count == 2, $"初始已知菜谱 {G.discovered.Count}（米饭、清炒时蔬）");
            yield return Frames(3);
        }

        // ───────────────────────── 2. 移动 / 切换区域 ─────────────────────────
        IEnumerator MoveTest()
        {
            log.AppendLine("== 移动与切换空间");
            var p0 = P.transform.position;
            InnInput.SimMove = new Vector2(0, 1);
            yield return new WaitForSeconds(0.6f);
            InnInput.SimMove = Vector2.zero;
            float moved = Vector3.Distance(p0, P.transform.position);
            Check(moved > 1f, $"WASD 移动 {moved:0.0} 米");
            foreach (var (key, zone) in new[] { (KeyCode.Alpha1, Zone.Garden), (KeyCode.Alpha3, Zone.Dining), (KeyCode.Alpha2, Zone.Kitchen) })
            {
                InnInput.Press(key);
                yield return Frames(3);
                Check(G.grid.ZoneOf(P.transform.position) == zone, $"按 {key} 前往 {Names.Of(zone)}");
            }
        }

        // ───────────────────────── 3. 种菜 ─────────────────────────
        Plot[] farm;

        IEnumerator FarmTest()
        {
            log.AppendLine("== 菜园（播种 / 浇水 / 收获 / 鸡舍）");
            farm = G.plots.OrderBy(p => p.cell.y).ThenBy(p => p.cell.x).ToArray();   // (2,5)(3,5)(4,5)(2,6)(3,6)(4,6)
            string[] seeds = { "seed_rice", "seed_rice", "seed_greens", "seed_scallion", "seed_tomato" };
            for (int i = 0; i < seeds.Length; i++)
            {
                Select(seeds[i]);
                yield return Press(farm[i], KeyCode.E, "播种");
            }
            Check(farm[0].crop != null && farm[0].crop.id == "rice", "选中稻米种子按 E 播种");
            Check(farm.Take(5).All(p => p.crop != null), $"5 块地都种上了（{string.Join(",", farm.Take(5).Select(p => p.crop ? p.crop.displayName : "空"))}）");
            Check(Bag("seed_rice") == 8, $"种子扣除（稻米种子剩 {Bag("seed_rice")}）");
            Check(farm[0].NeedsWater, "稻米没浇水时提示缺水、不生长");
            float g0 = farm[0].growth;
            yield return new WaitForSeconds(0.5f);
            Check(Mathf.Approximately(farm[0].growth, g0), "缺水时停止生长");
            P.selected = -1;
            yield return Press(farm[0], KeyCode.E, "浇水");
            Check(farm[0].Watered, "按 E 浇水");
            Check(!farm[3].NeedsWater, "葱不需要浇水");

            // 加速等成熟，缺水就浇
            Speed = 12;
            yield return WaitUntil(() => farm.Take(5).All(p => p.Ripe), 40, () =>
            {
                foreach (var p in farm.Take(5)) if (p.NeedsWater) p.Water(G.db.waterSeconds);
            });
            Speed = 1;
            Check(farm.Take(5).All(p => p.Ripe), "全部成熟");
            Shot("garden_ripe", farm[1].Center, 4.5f);
            for (int i = 0; i < 5; i++) yield return Press(farm[i], KeyCode.E, "收获");
            Check(Bag("rice") == 6 && Bag("greens") == 2 && Bag("scallion") == 2 && Bag("tomato") == 3,
                $"收获进背包：稻米 {Bag("rice")}/6 青菜 {Bag("greens")}/2 葱 {Bag("scallion")}/2 番茄 {Bag("tomato")}/3");
            Check(farm.Take(5).All(p => p.Empty && p.lastCrop != null), "收获后变回空地并记住上一茬作物");

            var coop = G.buildings.OfType<Producer>().First();
            Speed = 12;
            yield return WaitUntil(() => coop.stock >= 3, 30);
            Speed = 1;
            yield return Press(coop, KeyCode.E, "鸡舍");
            Check(Bag("egg") >= 3, $"鸡舍拿到鸡蛋 ×{Bag("egg")}");
        }

        // ───────────────────────── 4. 厨房 ─────────────────────────
        IEnumerator CookTest()
        {
            log.AppendLine("== 厨房（菜谱合成 / 发现 / 黑暗料理）");
            var steam = Find<Stove>("stove_steam");
            var fry = Find<Stove>("stove_fry");
            var boil = Find<Stove>("stove_boil");

            Select("rice");
            yield return Press(steam, KeyCode.E, "蒸笼放稻米");
            Check(steam.inputs.Count == 1 && Bag("rice") == 5, "稻米放入蒸笼");
            yield return Press(steam, KeyCode.F, "蒸笼开火");
            Check(steam.cooking && steam.current != null && steam.current.id == "rice_bowl", "稻米（蒸）= 米饭");
            Speed = 6;
            yield return WaitUntil(() => !steam.cooking, 10);
            Speed = 1;
            P.selected = -1;
            yield return Press(steam, KeyCode.E, "取出米饭");
            Check(Bag("rice_bowl") == 1, "取出米饭");

            int found0 = G.Stats.discovered;
            Select("egg");
            yield return Press(fry, KeyCode.E, "炒锅放鸡蛋");
            Select("rice_bowl");
            yield return Press(fry, KeyCode.E, "炒锅放米饭");
            yield return Press(fry, KeyCode.F, "炒锅开火");
            Check(fry.cooking && fry.current != null && fry.current.id == "egg_rice", "鸡蛋 + 米饭（炒）= 蛋炒饭（不分顺序）");
            Speed = 6;
            yield return WaitUntil(() => !fry.cooking, 10);
            Speed = 1;
            Check(G.discovered.Any(r => r.id == "egg_rice") && G.Stats.discovered == found0 + 1, "首次做出蛋炒饭 → 发现新菜谱");
            Check(G.MenuDishes().Any(d => d.id == "egg_rice"), "新菜谱自动加入菜单");
            P.selected = -1;
            yield return Press(fry, KeyCode.E, "取出蛋炒饭");
            Check(P.bag.Count(G.db.Item("egg_rice")) == 1, "取出蛋炒饭");

            // 同样食材换方式 → 不同的菜
            Select("tomato"); yield return Press(boil, KeyCode.E);
            Select("egg"); yield return Press(boil, KeyCode.E);
            Check(boil.Resolve() != null && boil.Resolve().id == "tomato_soup", "番茄 + 鸡蛋（煮）= 番茄蛋汤（炒则是番茄炒蛋）");
            P.selected = -1;
            yield return Press(boil, KeyCode.E, "拿回食材");
            Check(boil.inputs.Count == 0 && Bag("tomato") == 3, "空手 E 拿回灶台里的食材");

            // 黑暗料理
            Select("scallion"); yield return Press(steam, KeyCode.E);
            Select("scallion"); yield return Press(steam, KeyCode.E);
            yield return Press(steam, KeyCode.F, "葱+葱 蒸");
            Check(steam.cooking && steam.current == null, "葱 + 葱（蒸）没有菜谱 → 黑暗料理");
            Speed = 6;
            yield return WaitUntil(() => !steam.cooking, 10);
            Speed = 1;
            Check(steam.output.Count(G.db.darkDish) == 1, "产出黑暗料理");
            P.selected = -1;
            yield return Press(steam, KeyCode.E);
            Shot("kitchen", Zone.Kitchen, 5.5f);
        }

        // ───────────────────────── 5. 上菜 ─────────────────────────
        IEnumerator ServeTest()
        {
            log.AppendLine("== 饭厅（顾客入座 / 点菜 / 上菜 / 结账）");
            int gold0 = G.gold, rep0 = G.rep;
            G.forcedOrder = G.db.Item("egg_rice");
            var cu = G.SpawnCustomer(G.db.customers.First(c => c.id == "scholar"));
            Speed = 3;
            yield return WaitUntil(() => cu.state == Customer.State.Waiting, 20);
            Speed = 1;
            Check(cu.state == Customer.State.Waiting && cu.seat != null, $"顾客进门入座（状态 {cu.state}）");
            Check(cu.orders.Count == 1 && cu.orders[0].id == "egg_rice", "点了蛋炒饭");
            Shot("dining_order", Zone.Dining, 5.5f);
            P.selected = -1;
            yield return Press(cu.seat, KeyCode.E, "对着客人按 E 上菜");
            Check(cu.state == Customer.State.Eating || cu.served.Count == 1, "上菜成功（自动从背包找客人点的菜）");
            Check(P.bag.Count(G.db.Item("egg_rice")) == 0, "蛋炒饭从背包扣除");
            yield return new WaitForSeconds(0.5f);
            Shot("dining_eating", Zone.Dining, 5.5f);
            Speed = 4;
            yield return WaitUntil(() => cu == null, 30);
            Speed = 1;
            Check(cu == null, "吃完去柜台结账后离开");
            Check(G.gold >= gold0 + 18, $"收入 {G.gold - gold0} 金（菜价 18 + 小费）");
            Check(G.rep > rep0, $"名望 +{G.rep - rep0}");
            Check(G.Stats.customersPaid == 1, "结账人数 1");
            G.forcedOrder = null;
        }

        IEnumerator MenuToggleTest()
        {
            log.AppendLine("== 菜单 Active / Inactive");
            var egg = G.db.Item("egg_rice");
            G.forcedOrder = egg;
            var cu = G.SpawnCustomer(G.db.customers.First(c => c.id == "scholar"));
            Speed = 3;
            yield return WaitUntil(() => cu.state == Customer.State.Waiting, 20);
            Speed = 1;
            G.forcedOrder = null;
            Check(cu.orders.Contains(egg), "测试客人点了蛋炒饭");
            G.net.ToggleDish(egg, false);
            Check(!G.MenuDishes().Contains(egg), "设为 Inactive 后不在菜单上");
            Check(!cu.orders.Contains(egg) && cu.orders.All(o => !G.disabledDishes.Contains(o)),
                $"已点未上的蛋炒饭换成在售菜（现在：{string.Join("、", cu.orders.Select(o => o.displayName))}）");
            var others = G.MenuDishes().ToList();
            foreach (var d in others) G.net.ToggleDish(d, false);
            Check(!G.MenuDishes().Any() && cu.orders.Count == 0, "全部 Inactive → 菜单为空、订单取消");
            int left0 = G.Stats.customersLeft;
            yield return WaitUntil(() => cu == null, 20);
            Check(cu == null && G.Stats.customersLeft > left0, "一道都没上的客人直接离开");
            foreach (var d in others.Append(egg)) G.net.ToggleDish(d, true);
            Check(G.MenuDishes().Contains(egg) && G.MenuDishes().Count() == others.Count + 1, "重新设为 Active 后回到菜单");
        }

        // ───────────────────────── 6. 建造 ─────────────────────────
        IEnumerator BuildTest()
        {
            log.AppendLine("== 建造（网格放置 / 区域限制 / 座位判定 / 拆除）");
            InnInput.Press(KeyCode.B);
            yield return Frames(2);
            Check(G.build.active, "按 B 进入建造模式");
            int gold0 = G.gold, seats0 = G.chairs.Count(c => c.IsSeat), decor0 = G.DecorBonus;
            var table = G.build.TryPlace(Def("table"), new Vector2Int(22, 1), 0);
            Check(table != null && G.gold == gold0 - 40, "饭厅放方桌（-40 金）");
            var chair = G.build.TryPlace(Def("chair"), new Vector2Int(24, 1), 3) as Chair;
            Check(chair != null && chair.IsSeat && G.chairs.Count(c => c.IsSeat) == seats0 + 1, "椅子挨着桌子 → 座位 +1");
            var lonely = G.build.TryPlace(Def("chair"), new Vector2Int(25, 8), 0) as Chair;
            Check(lonely != null && !lonely.IsSeat, "没挨着桌子的椅子不算座位");
            Check(G.build.TryPlace(Def("table"), new Vector2Int(2, 1), 0) == null, "方桌不能放在菜园");
            Check(G.build.TryPlace(Def("plot"), new Vector2Int(24, 3), 0) == null, "耕地不能放在饭厅");
            Check(G.build.TryPlace(Def("chair"), new Vector2Int(22, 1), 0) == null, "不能叠放在已占用的格子");
            G.build.TryPlace(Def("lantern"), new Vector2Int(21, 0), 0);
            Check(G.DecorBonus > decor0, $"放灯笼 → 环境分 {decor0} → {G.DecorBonus}");
            int g1 = G.gold;
            Check(G.build.TryRemove(new Vector2Int(25, 8)) && G.gold == g1 + 10, "拆除返还 100%");
            int r0 = G.build.dir;
            InnInput.Press(KeyCode.R);
            yield return Frames(2);
            Check(G.build.dir == (r0 + 1) % 4, "R 旋转");
            G.build.dir = 0;
            InnInput.Press(KeyCode.B);
            yield return Frames(2);
            Check(!G.build.active, "再按 B 退出建造");
        }

        // ───────────────────────── 7. 自动化 ─────────────────────────
        IEnumerator AutomationTest()
        {
            log.AppendLine("== 自动化（洒水器 / 收割机关 / 播种机关 / 传送带 / 机关手 / 自动灶台）");
            G.gold += 2000;
            var B = G.build;
            // 菜园自动线：收割机关(9,2)朝东 → 传送带 (10,2)(11,2)(12,2) 穿过区门 → 厨房箱子(13,2)
            var plotsAuto = new List<Plot>();
            foreach (var c in new[] { new Vector2Int(8, 1), new Vector2Int(9, 1), new Vector2Int(10, 1), new Vector2Int(8, 2), new Vector2Int(8, 3), new Vector2Int(9, 3), new Vector2Int(10, 3) })
                plotsAuto.Add(B.TryPlace(Def("plot"), c, 0) as Plot);
            Check(plotsAuto.All(p => p != null), "放置 7 块耕地");
            var harvester = B.TryPlace(Def("harvester"), new Vector2Int(9, 2), 1) as Harvester;
            var belts = new[] { 10, 11, 12 }.Select(x => B.TryPlace(Def("belt"), new Vector2Int(x, 2), 1) as Belt).ToList();
            var sprinkler = B.TryPlace(Def("sprinkler"), new Vector2Int(7, 2), 0);
            var seeder = B.TryPlace(Def("seeder"), new Vector2Int(9, 4), 2);
            var seedBox = B.TryPlace(Def("box"), new Vector2Int(9, 5), 0) as Container;
            Check(harvester && belts.All(b => b) && sprinkler && seeder && seedBox, "放置收割机关、3 格传送带（跨区）、洒水器、播种机关、种子箱");

            // 商店买葱种子放进种子箱
            Check(G.BuySeed(G.db.Crop("scallion"), 20), "商店买 20 个葱种子");
            Select("seed_scallion");
            yield return Press(seedBox, KeyCode.E, "种子放进箱子");
            Check(seedBox.bag.Count(G.db.Item("seed_scallion")) >= 20, $"种子箱里有 {seedBox.bag.Count(G.db.Item("seed_scallion"))} 个葱种子");

            // 洒水器覆盖的地：手动种稻米
            Select("seed_rice");
            yield return Press(plotsAuto[0], KeyCode.E, "种稻米(8,1)");
            yield return Frames(3);
            Check(plotsAuto[0].crop != null && plotsAuto[0].Watered, "洒水器自动给周围 3×3 的稻米浇水");

            var kbox = Find<Container>("box");
            kbox = G.containers.First(c => c.cell == new Vector2Int(13, 2));
            int sc0 = kbox.bag.Count(G.db.Item("scallion"));
            Speed = 10;
            yield return WaitUntil(() => plotsAuto.Skip(4).Count(p => p.crop != null) == 3, 10);
            Check(plotsAuto.Skip(4).All(p => p.crop != null && p.crop.id == "scallion"), "播种机关从后方箱子取种子，播种周围空地");
            yield return WaitUntil(() => kbox.bag.Count(G.db.Item("scallion")) - sc0 >= 4, 30);
            Shot("garden_automation", new Vector3(10, 0, 3), 5f);
            Check(kbox.bag.Count(G.db.Item("scallion")) - sc0 >= 4, $"收割机关 → 传送带 → 厨房箱子：葱 +{kbox.bag.Count(G.db.Item("scallion")) - sc0}");
            Check(G.Stats.beltMoves > 0, $"传送带转运 {G.Stats.beltMoves} 次");
            yield return WaitUntil(() => plotsAuto.Skip(4).All(p => p.crop != null), 10);
            Check(plotsAuto.Skip(4).All(p => p.crop != null), "收割后播种机关自动补种（循环）");
            Speed = 1;

            // 厨房：箱子 → 机关手 → 自动蒸笼（米饭）→ 机关手 → 传送带 → 出菜口
            var steam = Find<Stove>("stove_steam");
            var box2 = B.TryPlace(Def("box"), new Vector2Int(17, 6), 0) as Container;
            var insIn = B.TryPlace(Def("inserter"), new Vector2Int(17, 7), 0) as Inserter;
            var insOut = B.TryPlace(Def("inserter"), new Vector2Int(18, 8), 1) as Inserter;
            var b1 = B.TryPlace(Def("belt"), new Vector2Int(19, 8), 2);
            var b2 = B.TryPlace(Def("belt"), new Vector2Int(19, 7), 2);
            var b3 = B.TryPlace(Def("belt"), new Vector2Int(19, 6), 2);
            Check(box2 && insIn && insOut && b1 && b2 && b3, "放置 箱子 → 机关手 → 蒸笼 → 机关手 → 传送带 ×3 → 出菜口");
            yield return Press(steam, KeyCode.G, "G 设定自动菜谱");
            Check(steam.fixedRecipe != null && steam.fixedRecipe.id == "rice_bowl", "蒸笼自动菜谱 = 米饭");
            var riceItem = new Item(G.db.Item("rice"));
            P.bag.Remove(riceItem, 2);   // 留 2 个给后面的厨师测试
            Select("rice");
            yield return Press(box2, KeyCode.E, "稻米放进箱子");
            var pass = G.passWindows[0];
            int rb0 = pass.bag.Count(G.db.Item("rice_bowl"));
            Speed = 6;
            yield return WaitUntil(() => pass.bag.Count(G.db.Item("rice_bowl")) - rb0 >= 2, 30);
            Check(pass.bag.Count(G.db.Item("rice_bowl")) - rb0 >= 2, $"全自动出餐：出菜口收到米饭 ×{pass.bag.Count(G.db.Item("rice_bowl")) - rb0}");
            Check(insIn.moved >= 2 && insOut.moved >= 2, $"机关手搬运 入料 {insIn.moved} 次 / 出料 {insOut.moved} 次");
            Check(pass.bag.slots.Where(s => s.item.def.id == "rice_bowl").All(s => s.item.q == 1), "自动灶台产出固定 1★");
            Speed = 1;
            Shot("kitchen_automation", new Vector3(17, 0, 7), 4.5f);
            // 测完把剩下的稻米拿回来，取消自动菜谱（后面让厨师用蒸笼）
            P.selected = -1;
            yield return Press(box2, KeyCode.E);
            steam.SetFixed(null);
            Speed = 6;
            yield return WaitUntil(() => !steam.cooking && steam.output.IsEmpty && new[] { b1, b2, b3 }.OfType<Belt>().All(b => !b.item.Valid), 10);
            yield return new WaitForSeconds(2f);
            Speed = 1;
            // 拆掉厨房机关手，不然厨师在蒸笼做的米饭会被机关手搬走
            Check(B.TryRemove(insIn.cell) && B.TryRemove(insOut.cell), "拆除机关手");
            foreach (var s in pass.bag.slots.ToList()) pass.bag.Remove(s.item, s.count);
            P.bag.Add(riceItem, 2);
        }

        // ───────────────────────── 8. 员工 ─────────────────────────
        IEnumerator StaffTest()
        {
            log.AppendLine("== 员工（招募 / 厨师 / 跑堂 / 园丁）");
            Check(G.recruits.Count == G.db.recruitCount, $"招募榜 {G.recruits.Count} 名候选");
            var r0 = G.recruits[0];
            Check(r0.stars >= 1 && r0.stars <= 5 && r0.wage == G.db.starRows[r0.stars - 1].wage, $"候选：{r0.Summary}");
            // 星级分布抽样
            var rng = new System.Random(1);
            var counts = new int[6];
            for (int i = 0; i < 5000; i++) counts[StaffData.Roll(G.db, rng).stars]++;
            Check(counts[1] > counts[2] && counts[2] > counts[3] && counts[3] > counts[4] && counts[4] > counts[5] && counts[5] > 0,
                $"星级出现率 1★~5★：{string.Join(" / ", counts.Skip(1))}（5000 次抽样）");

            G.RefreshRecruits(false);
            var chef = G.Hire(G.recruits[0], StaffJob.Chef);
            var waiter = G.Hire(G.recruits[0], StaffJob.Waiter);
            var gardener = G.Hire(G.recruits[0], StaffJob.Gardener);
            Check(chef && waiter && gardener && G.staff.Count == 3, $"雇佣厨师 / 跑堂 / 园丁（上限 {G.StaffCap}）");
            Check(G.Hire(StaffData.Roll(G.db, rng), StaffJob.Chef) == null || G.StaffCap > 3, "超过员工上限不能再雇");

            // 园丁：给一块地种上需要浇水的稻米，看园丁会不会去浇
            var plot = farm[5];
            plot.Plant(G.db.Crop("rice"));
            // 厨师：箱子里放稻米 + 鸡蛋，顾客点蛋炒饭 → 厨师先蒸米饭（中间食材）再炒
            var kbox = G.containers.First(c => c.cell == new Vector2Int(13, 2));
            Select("rice"); yield return Press(kbox, KeyCode.E, "稻米进厨房箱子");
            Select("egg"); yield return Press(kbox, KeyCode.E, "鸡蛋进厨房箱子");
            Check(kbox.bag.Count(G.db.Item("rice")) >= 1 && kbox.bag.Count(G.db.Item("egg")) >= 1, "厨房箱子里有稻米和鸡蛋");
            P.transform.position = G.ZoneSpawn(Zone.Garden);   // 玩家走开，全交给员工

            int cooked0 = G.Stats.cooked, served0 = G.Stats.dishesServed, paid0 = G.Stats.customersPaid;
            G.forcedOrder = G.db.Item("egg_rice");
            var cu = G.SpawnCustomer(G.db.customers.First(c => c.id == "scholar"));
            Speed = 4;
            bool watered = false, shot = false;
            yield return WaitUntil(() => G.Stats.customersPaid > paid0, 90, () =>
            {
                watered |= plot.Watered;
                if (!shot && cu != null && cu.state == Customer.State.Waiting && chef.status.StartsWith("烹饪")) { shot = true; Shot("staff_kitchen", new Vector3(15, 0, 6), 5.5f); }
            });
            Speed = 1;
            Check(G.Stats.cooked - cooked0 >= 2, $"厨师按点单做菜：先蒸米饭再炒蛋炒饭（共烹饪 {G.Stats.cooked - cooked0} 次）");
            Check(G.Stats.dishesServed > served0, "跑堂从出菜口取菜送到客人桌上");
            Check(G.Stats.customersPaid > paid0, "员工独立完成一单并结账");
            Check(watered, "园丁给缺水的稻米浇水");
            G.forcedOrder = null;

            // 再来几位随机顾客，菜单有啥点啥（米饭、清炒时蔬、蛋炒饭）
            Select("greens"); P.transform.position = GridMap.ToWorld(new Vector2Int(13, 3)); yield return Press(kbox, KeyCode.E);
            G.spawnEnabled = true;
            G.spawnTimer = 0;
            Speed = 4;
            yield return new WaitForSeconds(5f);
            Shot("dining_busy", Zone.Dining, 6f);
            G.spawnEnabled = false;
            Speed = 1;
            log.AppendLine($"  信息 员工状态：{string.Join("；", G.staff.Select(s => s.Label()))}");
        }

        // ───────────────────────── 9. 结算 ─────────────────────────
        IEnumerator DayEndTest()
        {
            log.AppendLine("== 每日结算");
            keepDayOpen = false;
            G.spawnEnabled = false;
            G.EndDayNow();
            Speed = 8;
            yield return WaitUntil(() => G.summary != null, 120);
            Speed = 1;
            var s = G.summary;
            Check(s != null, "打烊后客人走光 → 显示结算");
            if (s == null) yield break;
            int wages = G.staff.Sum(x => x.data.wage);
            Check(s.wages == wages && s.upkeep == G.db.upkeepBase + Mathf.RoundToInt(G.BuildValue * G.db.upkeepPerBuildValue),
                $"工钱 {s.wages}（应为 {wages}）维护费 {s.upkeep}");
            Check(G.Paused, "结算面板打开时暂停");
            log.AppendLine($"  信息 第 {s.day} 天：菜钱 {s.income} 小费 {s.tips} 工钱 {s.wages} 维护 {s.upkeep} 接待 {s.served} 流失 {s.left} 名望 {s.rep}");
            InnInput.Press(KeyCode.Return);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(G.summary == null && G.day == 2 && G.Open, "Enter 开始第 2 天");
        }

        // ───────────────────────── 10. 联机（本编辑器当主机 + 5 个打包客户端进程）─────────────────────────
        IEnumerator NetTest()
        {
            log.AppendLine("== 联机（主机 + 打包客户端 ×5，上限 5 人）");
            var exe = Path.GetFullPath(InnSetup.PlayerPath);
            if (!SessionState.GetBool("INN_PLAYER_BUILT", false) || !File.Exists(exe))
            {
                Check(false, "打包失败或找不到客户端 " + exe + "（用 SmokeTest.BuildAndRun 跑）");
                yield break;
            }
            // 场地准备：解雇员工（不然园丁会抢着种地），清空 6 块耕地
            foreach (var s in G.staff.ToList()) G.Fire(s);
            foreach (var p in farm) p.ClearCrop();
            G.gold += 500;
            Speed = 1;

            // 协议自检：快照能写出来
            var snap = G.net.WriteSnapshot();
            Check(snap.Length > 200, $"快照 {snap.Length / 1024f:0.0} KB（{G.buildings.Count} 个建筑）");

            int port = 7790 + UnityEngine.Random.Range(1, 200);
            Check(G.net.Host(port), $"开房间（端口 {port}）");
            if (!G.net.IsHost) yield break;

            var procs = new List<System.Diagnostics.Process>();
            var logs = new List<string>();
            Directory.CreateDirectory("Logs");
            for (int i = 0; i < 5; i++)
            {
                var botLog = Path.GetFullPath($"Logs/net_bot{i}.txt");
                if (File.Exists(botLog)) File.Delete(botLog);
                logs.Add(botLog);
                var psi = new System.Diagnostics.ProcessStartInfo(exe,
                    $"-batchmode -nographics -name 测试{i} -join 127.0.0.1 -port {port} -nettest \"{botLog}\" -logFile \"{Path.GetFullPath($"Logs/net_bot{i}.log")}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                procs.Add(System.Diagnostics.Process.Start(psi));
                yield return new WaitForSecondsRealtime(1.5f);   // 错开，保证第 5 个一定是被拒的那个
            }

            yield return WaitUntil(() => G.players.Count >= 5, 60);
            Check(G.players.Count == 5, $"5 人到齐（主机 + 4 个客户端）：{string.Join("、", G.players.Select(p => p.playerName))}");

            string Read(string f) { try { return File.Exists(f) ? File.ReadAllText(f) : ""; } catch { return ""; } }
            yield return WaitUntil(() => logs.All(f => { var t = Read(f); return t.Contains("BOT DONE") || t.Contains("REJECTED") || t.Contains("FAIL") || t.Contains("TIMEOUT"); }), 120);
            yield return new WaitForSecondsRealtime(1f);

            int done = 0, rejected = 0;
            for (int i = 0; i < logs.Count; i++)
            {
                var t = Read(logs[i]);
                if (t.Contains("BOT DONE")) done++;
                if (t.Contains("REJECTED")) rejected++;
                foreach (var line in t.Split('\n').Where(l => l.StartsWith("FAIL") || l.StartsWith("TIMEOUT")))
                    Check(false, $"客户端{i}：{line}");
            }
            Check(done == 4, $"4 个客户端完成全部步骤（连接 → 同步 → 移动 → 买种子 → 播种 → 建造）：{done}/4");
            Check(rejected == 1, $"第 6 人被拒（房间满 5 人）：{rejected}/1");
            log.AppendLine("  信息 客户端0 日志：" + Read(logs[0]).Replace("\n", " | "));

            var planted = farm.Count(p => p.crop != null && p.crop.id == "scallion");
            Check(planted == 4, $"主机这边看到 4 块地被客户端种上了葱：{planted}");
            int plants = Enumerable.Range(24, 4).Count(x => G.grid.At(new Vector2Int(x, 8))?.def.id == "plant");
            Check(plants == 4, $"主机这边看到客户端放的盆栽 {plants}/4");
            Check(G.players.Where(p => !p.isLocal).All(p => p.bag.Count(G.db.Item("seed_scallion")) >= 4), "每个客户端掌柜有自己的背包（买的种子在各自背包里）");
            Check(G.net.snapshotsSent > 20 && G.net.commandsExecuted >= 12, $"发出快照 {G.net.snapshotsSent} 次，执行客户端指令 {G.net.commandsExecuted} 条");
            Shot("net_garden", farm[1].Center + new Vector3(1, 0, 0), 4.5f);
            Shot("overview", new Vector3(15, 0, 5), 11f);

            // 关房间 → 客户端断开后自动退出
            G.net.Leave();
            float t0 = Time.realtimeSinceStartup;
            while (procs.Any(p => !p.HasExited) && Time.realtimeSinceStartup - t0 < 20) yield return null;
            Check(procs.All(p => p.HasExited), "关闭房间后客户端全部断开退出");
            foreach (var p in procs) if (!p.HasExited) p.Kill();
            Check(G.players.Count == 1 && !G.net.Online, "主机回到单机");
        }
    }
}
