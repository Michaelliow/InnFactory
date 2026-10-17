using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Inn.EditorTools
{
    /// <summary>
    /// 全部游戏数值的唯一来源。生成到 Assets/Game/Data/**（每次整体重建）和 Resources/GameDatabase.asset。
    /// 改数值请改这里，然后 InnFactory → Setup → Run All。对应设计文档 Docs/GDD.md 的各个表格。
    /// </summary>
    public static class InnDataFactory
    {
        const string DataDir = "Assets/Game/Data";
        const string DbPath = "Assets/Game/Resources/GameDatabase.asset";
        const string MatPath = "Assets/Game/Resources/InnLit.mat";

        static readonly Dictionary<string, ItemDef> items = new Dictionary<string, ItemDef>();
        static readonly List<CropDef> crops = new List<CropDef>();
        static readonly List<RecipeDef> recipes = new List<RecipeDef>();
        static readonly List<BuildableDef> buildables = new List<BuildableDef>();
        static readonly List<CustomerDef> customers = new List<CustomerDef>();

        [MenuItem("InnFactory/Setup/2. Build Data")]
        public static GameDatabase Build()
        {
            items.Clear(); crops.Clear(); recipes.Clear(); buildables.Clear(); customers.Clear();
            if (AssetDatabase.IsValidFolder(DataDir)) AssetDatabase.DeleteAsset(DataDir);
            InnSetup.EnsureFolder(DataDir + "/Items");
            InnSetup.EnsureFolder(DataDir + "/Crops");
            InnSetup.EnsureFolder(DataDir + "/Recipes");
            InnSetup.EnsureFolder(DataDir + "/Buildables");
            InnSetup.EnsureFolder(DataDir + "/Customers");
            InnSetup.EnsureFolder("Assets/Game/Resources");

            BuildCrops();
            BuildRecipes();
            BuildBuildables();
            BuildCustomers();

            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DbPath);
            if (db == null) { db = ScriptableObject.CreateInstance<GameDatabase>(); AssetDatabase.CreateAsset(db, DbPath); }
            db.items = items.Values.ToArray();
            db.crops = crops.ToArray();
            db.recipes = recipes.ToArray();
            db.buildables = buildables.ToArray();
            db.customers = customers.ToArray();
            db.darkDish = items["dark"];
            db.baseMaterial = BuildMaterial();
            BuildBalance(db);
            BuildStaff(db);
            BuildLayout(db);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Validate(db);
            Debug.Log($"[InnData] items={db.items.Length} crops={db.crops.Length} recipes={db.recipes.Length} buildables={db.buildables.Length}");
            return db;
        }

        // ───────────────────────── 作物（GDD 3.1）─────────────────────────
        static void BuildCrops()
        {
            //       id         名字   种子价 生长秒 产出 需水  卖价 颜色
            Crop("rice",     "稻米", 2, 60, 3, true, 2, new Color(0.95f, 0.85f, 0.45f));
            Crop("greens",   "青菜", 1, 40, 2, true, 2, new Color(0.35f, 0.8f, 0.3f));
            Crop("scallion", "葱",   1, 30, 2, false, 1, new Color(0.55f, 0.9f, 0.45f));
            Crop("tomato",   "番茄", 3, 80, 3, true, 3, new Color(0.95f, 0.25f, 0.2f));
            Crop("radish",   "萝卜", 2, 60, 2, true, 3, new Color(0.95f, 0.95f, 0.9f));
            Crop("wheat",    "小麦", 2, 70, 3, true, 2, new Color(0.85f, 0.7f, 0.35f));
            Crop("soy",      "黄豆", 2, 70, 3, true, 2, new Color(0.9f, 0.8f, 0.4f));
            Crop("chili",    "辣椒", 4, 90, 3, true, 4, new Color(0.85f, 0.1f, 0.1f));

            // 畜舍产物（GDD 3.2）
            Item("egg", "鸡蛋", ItemKind.Raw, 3, new Color(1f, 0.93f, 0.8f));
            Item("pork", "猪肉", ItemKind.Raw, 8, new Color(0.95f, 0.55f, 0.55f));
            Item("fish", "鱼", ItemKind.Raw, 10, new Color(0.5f, 0.7f, 0.85f));
        }

        static void Crop(string id, string name, int seedPrice, float grow, int yield, bool water, int sell, Color c)
        {
            var produce = Item(id, name, ItemKind.Raw, sell, c);
            var seed = Item("seed_" + id, name + "种子", ItemKind.Seed, seedPrice, Color.Lerp(c, new Color(0.45f, 0.3f, 0.15f), 0.6f));
            var crop = New<CropDef>("Crops", id);
            crop.id = id; crop.displayName = name; crop.seed = seed; crop.produce = produce; crop.seedPrice = seedPrice;
            crop.growSeconds = grow; crop.yield = yield; crop.needsWater = water; crop.color = c;
            seed.crop = crop;
            crops.Add(crop);
        }

        // ───────────────────────── 菜谱（GDD 5.2）─────────────────────────
        static void BuildRecipes()
        {
            //     产出id     名字       类型              售价 颜色                                 方式                秒  初始已知  食材
            Dish("rice_bowl",  "米饭",     DishTag.Staple,    6, new Color(1f, 1f, 0.95f),   CookMethod.Steam,   6, true,  "rice");
            Dish("fried_greens","清炒时蔬", DishTag.Veg,      8, new Color(0.3f, 0.75f, 0.25f), CookMethod.StirFry, 5, true,  "greens");
            Dish("egg_rice",   "蛋炒饭",   DishTag.Staple,   18, new Color(1f, 0.85f, 0.3f), CookMethod.StirFry,  8, false, "egg", "rice_bowl");
            Dish("tomato_egg", "番茄炒蛋", DishTag.Veg,      20, new Color(1f, 0.5f, 0.2f),  CookMethod.StirFry,  8, false, "tomato", "egg");
            Dish("tomato_soup","番茄蛋汤", DishTag.Soup,     16, new Color(0.95f, 0.45f, 0.3f), CookMethod.Boil,  10, false, "tomato", "egg");
            Dish("egg_soup",   "葱花蛋汤", DishTag.Soup,     14, new Color(0.95f, 0.9f, 0.5f), CookMethod.Boil,   10, false, "scallion", "egg");
            Dish("radish_soup","萝卜汤",   DishTag.Soup,     12, new Color(0.9f, 0.9f, 0.8f), CookMethod.Boil,    10, false, "radish", "scallion");
            Dish("cold_radish","凉拌萝卜", DishTag.Cold,      9, new Color(0.95f, 0.85f, 0.85f), CookMethod.Cold,  4, false, "radish");
            Inter("flour",     "面粉",                         new Color(0.97f, 0.95f, 0.9f), CookMethod.Grind,    6, "wheat");
            Dish("mantou",     "馒头",     DishTag.Staple,   10, new Color(1f, 0.98f, 0.92f), CookMethod.Steam,   12, false, "flour");
            Dish("scallion_pancake","葱油饼", DishTag.Staple, 16, new Color(0.9f, 0.75f, 0.4f), CookMethod.PanFry, 10, false, "flour", "scallion");
            Dish("soymilk",    "豆浆",     DishTag.None,      5, new Color(0.98f, 0.96f, 0.85f), CookMethod.Grind, 6, false, "soy");
            Inter("tofu",      "豆腐",                         new Color(0.98f, 0.98f, 0.95f), CookMethod.Boil,    12, "soymilk");
            Dish("mapo_tofu",  "麻婆豆腐", DishTag.Signature, 48, new Color(0.85f, 0.25f, 0.15f), CookMethod.StirFry, 14, false, "tofu", "chili", "pork");
            Dish("braised_pork","红烧肉",  DishTag.Signature, 45, new Color(0.6f, 0.2f, 0.1f), CookMethod.Boil,    20, false, "pork", "scallion");
            Dish("grilled_fish","烤鱼",    DishTag.Signature, 55, new Color(0.8f, 0.4f, 0.15f), CookMethod.Roast,  18, false, "fish", "chili", "scallion");

            Item("dark", "黑暗料理", ItemKind.Junk, 1, new Color(0.2f, 0.12f, 0.25f));
        }

        static void Dish(string id, string name, DishTag tag, int price, Color c, CookMethod m, float secs, bool known, params string[] ing)
        {
            var it = Item(id, name, ItemKind.Dish, price, c);
            it.tag = tag;
            Recipe(it, m, secs, known, ing);
        }

        static void Inter(string id, string name, Color c, CookMethod m, float secs, params string[] ing)
            => Recipe(Item(id, name, ItemKind.Intermediate, 0, c), m, secs, false, ing);

        static void Recipe(ItemDef output, CookMethod m, float secs, bool known, string[] ing)
        {
            var r = New<RecipeDef>("Recipes", output.id);
            r.id = output.id; r.output = output; r.method = m; r.seconds = secs; r.startKnown = known;
            r.ingredients = ing.Select(i => items[i]).ToArray();
            recipes.Add(r);
        }

        // ───────────────────────── 建筑 / 机关 / 家具（GDD 3.2 3.3 4 5.1 6.1）─────────────────────────
        static void BuildBuildables()
        {
            var wood = new Color(0.62f, 0.42f, 0.24f);
            var soil = new Color(0.42f, 0.28f, 0.16f);
            var metal = new Color(0.55f, 0.55f, 0.6f);
            var G = ZoneMask.Garden; var K = ZoneMask.Kitchen; var D = ZoneMask.Dining;

            B("plot", "耕地", BuildKind.Plot, 1, 1, 10, G, soil, 0.08f, false, "种植作物。E：播种 / 浇水 / 收获");
            Producer("coop", "鸡舍", 80, new Color(0.95f, 0.8f, 0.55f), "egg", 45, "每 45 秒产 1 个鸡蛋");
            Producer("pigpen", "猪圈", 300, new Color(0.9f, 0.6f, 0.6f), "pork", 120, "每 120 秒产 1 份猪肉（原型不需饲料）");
            Producer("fishpond", "鱼塘", 400, new Color(0.35f, 0.6f, 0.85f), "fish", 100, "每 100 秒产 1 条鱼");
            var box = B("box", "储物箱", BuildKind.Container, 1, 1, 15, ZoneMask.Any, wood, 0.6f, true, "16 格，每格 50");
            box.capacity = 16;

            Stove("stove_fry", "炒锅", CookMethod.StirFry, 50, new Color(0.25f, 0.25f, 0.28f));
            Stove("stove_boil", "汤锅", CookMethod.Boil, 50, new Color(0.5f, 0.35f, 0.25f));
            Stove("stove_steam", "蒸笼", CookMethod.Steam, 50, new Color(0.8f, 0.65f, 0.4f));
            Stove("board", "案板", CookMethod.Cold, 30, new Color(0.85f, 0.7f, 0.5f));
            Stove("pan", "平底锅", CookMethod.PanFry, 70, new Color(0.3f, 0.3f, 0.3f));
            Stove("mill", "石磨", CookMethod.Grind, 60, new Color(0.6f, 0.6f, 0.58f));
            Stove("oven", "烤炉", CookMethod.Roast, 150, new Color(0.7f, 0.3f, 0.2f));
            var pass = B("pass", "出菜口", BuildKind.PassWindow, 1, 1, 50, K | D, new Color(0.85f, 0.75f, 0.55f), 0.8f, true, "放菜品，跑堂从这里取菜上桌");
            pass.capacity = 8;

            B("table", "方桌", BuildKind.Table, 2, 2, 40, D, wood, 0.7f, true, "四周相邻的椅子算座位");
            B("chair", "椅子", BuildKind.Chair, 1, 1, 10, D, new Color(0.7f, 0.5f, 0.3f), 0.45f, true, "必须挨着桌子");
            Decor("plant", "盆栽", 1, 1, 15, 2, new Color(0.3f, 0.65f, 0.3f), 0.8f);
            Decor("lantern", "灯笼", 1, 1, 25, 4, new Color(0.95f, 0.3f, 0.2f), 1.4f);
            Decor("screen", "屏风", 2, 1, 30, 3, new Color(0.9f, 0.85f, 0.7f), 1.3f);
            Decor("painting", "字画", 1, 1, 60, 6, new Color(0.95f, 0.92f, 0.8f), 1.6f);
            B("counter", "柜台", BuildKind.Counter, 2, 1, 100, D, new Color(0.5f, 0.3f, 0.18f), 0.9f, true, "顾客结账处");

            var belt = B("belt", "木轨传送带", BuildKind.Belt, 1, 1, 2, ZoneMask.Any, new Color(0.55f, 0.4f, 0.25f), 0.12f, false, "1.5 格/秒；R 旋转方向");
            belt.speed = 1.5f;
            var fast = B("belt_fast", "铜轨传送带", BuildKind.Belt, 1, 1, 8, ZoneMask.Any, new Color(0.8f, 0.5f, 0.25f), 0.12f, false, "3 格/秒");
            fast.speed = 3f;
            var ins = B("inserter", "机关手", BuildKind.Inserter, 1, 1, 20, ZoneMask.Any, metal, 0.5f, true, "每 1.2 秒把后方格的物品搬到前方格");
            ins.interval = 1.2f;
            var spr = B("sprinkler", "洒水器", BuildKind.Sprinkler, 1, 1, 40, G, new Color(0.4f, 0.6f, 0.9f), 0.6f, true, "每 30 秒给周围 3×3 浇水");
            spr.interval = 30f;
            var har = B("harvester", "收割机关", BuildKind.Harvester, 1, 1, 80, G, new Color(0.75f, 0.6f, 0.3f), 0.7f, true, "收获周围 3×3 的成熟作物，从前方吐出");
            har.interval = 1f; har.capacity = 12;
            var sd = B("seeder", "播种机关", BuildKind.Seeder, 1, 1, 100, G, new Color(0.5f, 0.7f, 0.35f), 0.7f, true, "从后方箱子取种子，播种周围 3×3 的空地");
            sd.interval = 1f;
        }

        static BuildableDef B(string id, string name, BuildKind kind, int w, int h, int cost, ZoneMask zones, Color c, float height, bool solid, string blurb)
        {
            var b = New<BuildableDef>("Buildables", id);
            b.id = id; b.displayName = name; b.kind = kind; b.size = new Vector2Int(w, h); b.cost = cost; b.zones = zones;
            b.color = c; b.height = height; b.solid = solid; b.blurb = blurb;
            buildables.Add(b);
            return b;
        }

        static void Producer(string id, string name, int cost, Color c, string produce, float interval, string blurb)
        {
            var b = B(id, name, BuildKind.Coop, 2, 2, cost, ZoneMask.Garden, c, 0.9f, true, blurb);
            b.produce = items[produce]; b.interval = interval; b.capacity = 5;
        }

        static void Stove(string id, string name, CookMethod m, int cost, Color c)
        {
            var b = B(id, name, BuildKind.Stove, 1, 1, cost, ZoneMask.Kitchen, c, 0.8f, true, "烹饪方式：" + Names.Of(m) + "。E 放食材/取菜，F 开火，G 设定自动菜谱");
            b.method = m;
        }

        static void Decor(string id, string name, int w, int h, int cost, int decor, Color c, float height)
        {
            var b = B(id, name, BuildKind.Decor, w, h, cost, ZoneMask.Dining, c, height, true, "环境 +" + decor);
            b.decor = decor;
        }

        // ───────────────────────── 顾客（GDD 6.2）─────────────────────────
        static void BuildCustomers()
        {
            //     id         名字   预算       耐心  点菜数   最低品质 名望 权重 颜色                       偏好
            Cust("villager", "村民", 10, 25, 60, 1, 1, 1, 1, 3, new Color(0.55f, 0.45f, 0.35f), DishTag.Staple);
            Cust("scholar",  "书生", 15, 35, 90, 1, 2, 1, 1, 2, new Color(0.85f, 0.9f, 0.95f), DishTag.Soup, DishTag.Cold);
            Cust("swordsman","侠客", 30, 60, 45, 2, 2, 1, 3, 1.5f, new Color(0.3f, 0.3f, 0.35f), DishTag.Signature);
            Cust("merchant", "商人", 50, 120, 70, 2, 3, 2, 5, 1, new Color(0.75f, 0.55f, 0.15f), DishTag.Signature);
            Cust("noble",    "贵客", 150, 300, 80, 3, 3, 3, 7, 0.5f, new Color(0.6f, 0.2f, 0.6f), DishTag.Signature);
        }

        static void Cust(string id, string name, int bMin, int bMax, float patience, int oMin, int oMax, int minQ, int rep, float w, Color c, params DishTag[] likes)
        {
            var cd = New<CustomerDef>("Customers", id);
            cd.id = id; cd.displayName = name; cd.budget = new Vector2Int(bMin, bMax); cd.patience = patience;
            cd.orders = new Vector2Int(oMin, oMax); cd.minQuality = minQ; cd.minRepLevel = rep; cd.weight = w; cd.color = c; cd.likes = likes;
            customers.Add(cd);
        }

        // ───────────────────────── 经济 / 时间（GDD 6.3 8）─────────────────────────
        static void BuildBalance(GameDatabase db)
        {
            db.startGold = 500;
            db.startBag = new[]
            {
                new ItemCount { item = items["seed_rice"], count = 10 },
                new ItemCount { item = items["seed_greens"], count = 10 },
                new ItemCount { item = items["seed_scallion"], count = 5 },
                new ItemCount { item = items["seed_tomato"], count = 5 },
            };
            db.dayLengthSeconds = 480;
            db.repThresholds = new[] { 0, 60, 200, 450, 850, 1400, 2200, 3200, 4500, 6000 };
            db.upkeepBase = 10;
            db.upkeepPerBuildValue = 0.01f;
            db.waterSeconds = 30;

            db.arrivalBase = 20; db.arrivalPerLevel = 0.15f; db.doorWaitSeconds = 15; db.eatSeconds = 5;
            db.baseSatisfaction = 60; db.decorCap = 20; db.likeBonus = 10; db.darkPenalty = 30;
            db.waitPenaltyPerSec = 2; db.angryLeaveAt = 20;
            db.qualityPriceMul = new[] { 1f, 1f, 1.5f, 2.5f };
            db.qualitySatBonus = new[] { 0, 0, 10, 20 };

            db.playerSpeed = 4.5f; db.playerCooking = 30; db.interactRange = 1.7f;
        }

        // ───────────────────────── 员工（GDD 7.1）─────────────────────────
        static void BuildStaff(GameDatabase db)
        {
            db.starRows = new[]
            {
                Row(1, 50, 20, 40, 5, 25),
                Row(2, 28, 35, 55, 10, 35),
                Row(3, 15, 50, 70, 15, 45),
                Row(4, 6, 65, 85, 20, 55),
                Row(5, 1, 80, 100, 30, 65),
            };
            db.recruitCount = 3; db.recruitRefreshCost = 30; db.staffBaseCap = 2;
            db.surnames = new[] { "李", "王", "张", "刘", "陈", "柳", "林", "苏", "沈", "叶", "唐", "白", "秦", "韩", "云" };
            db.givenNames = new[] { "阿福", "小翠", "青青", "大壮", "二牛", "月儿", "三娘", "子安", "若水", "长生", "春桃", "铁柱", "灵儿", "逍遥", "如意" };
        }

        static StarRow Row(int stars, float weight, int mainMin, int mainMax, int otherMin, int otherMax) => new StarRow
        {
            stars = stars, weight = weight,
            mainRange = new Vector2Int(mainMin, mainMax), otherRange = new Vector2Int(otherMin, otherMax),
            wage = Mathf.RoundToInt(20f * Mathf.Pow(stars, 1.6f) / 5f) * 5,
        };

        // ───────────────────────── 初始布局（GDD 8.1）─────────────────────────
        // 菜园 x 0-11 | 厨房 x 12-19 | 饭厅 x 20-29，y 0-9；区门在 y 3-6
        static void BuildLayout(GameDatabase db)
        {
            db.mapHeight = 10; db.gardenWidth = 12; db.kitchenWidth = 8; db.diningWidth = 10;
            db.doorRows = new Vector2Int(3, 6);
            var L = new List<LayoutEntry>();
            void Put(string id, int x, int y, int dir = 0, string content = null, int count = 0) =>
                L.Add(new LayoutEntry { def = db.Buildable(id), cell = new Vector2Int(x, y), dir = dir, content = content != null ? items[content] : null, count = count });

            for (int x = 2; x <= 4; x++) for (int y = 5; y <= 6; y++) Put("plot", x, y);
            Put("coop", 7, 6);
            Put("box", 5, 2);

            Put("stove_fry", 13, 8); Put("stove_boil", 15, 8); Put("stove_steam", 17, 8);
            Put("box", 13, 2);
            Put("pass", 19, 5);

            Put("table", 22, 5); Put("chair", 21, 5, 1); Put("chair", 24, 6, 3);
            Put("table", 26, 5); Put("chair", 25, 5, 1); Put("chair", 28, 6, 3);
            Put("counter", 27, 1);
            Put("plant", 20, 9); Put("lantern", 29, 9);
            db.startLayout = L.ToArray();
        }

        // ───────────────────────── 工具 ─────────────────────────
        static ItemDef Item(string id, string name, ItemKind kind, int price, Color c)
        {
            var it = New<ItemDef>("Items", id);
            it.id = id; it.displayName = name; it.kind = kind; it.price = price; it.color = c;
            items[id] = it;
            return it;
        }

        static T New<T>(string folder, string id) where T : ScriptableObject
        {
            var a = ScriptableObject.CreateInstance<T>();
            a.name = id;
            AssetDatabase.CreateAsset(a, $"{DataDir}/{folder}/{id}.asset");
            return a;
        }

        static Material BuildMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, MatPath); }
            mat.shader = sh;
            mat.SetFloat("_Smoothness", 0.15f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>数据完整性：每个菜谱的食材都要有来源（作物 / 畜舍 / 其他菜谱）</summary>
        public static List<string> Validate(GameDatabase db)
        {
            var problems = new List<string>();
            var sources = new HashSet<ItemDef>();
            foreach (var c in db.crops) sources.Add(c.produce);
            foreach (var b in db.buildables) if (b.produce) sources.Add(b.produce);
            foreach (var r in db.recipes) sources.Add(r.output);
            foreach (var r in db.recipes)
                foreach (var i in r.ingredients)
                    if (!sources.Contains(i)) problems.Add($"菜谱 {r.output.displayName} 的食材 {i.displayName} 没有来源");
            var keys = new HashSet<string>();
            foreach (var r in db.recipes)
                if (!keys.Add(r.Key())) problems.Add("重复的菜谱组合：" + r.Key());
            foreach (var r in db.recipes)
            {
                if (r.ingredients.Length < 1 || r.ingredients.Length > 3) problems.Add($"菜谱 {r.id} 食材数 {r.ingredients.Length}（应为 1~3）");
                if (!db.buildables.Any(b => b.kind == BuildKind.Stove && b.method == r.method)) problems.Add($"菜谱 {r.id} 的烹饪方式 {r.method} 没有灶台");
            }
            foreach (var p in problems) Debug.LogError("[InnData] " + p);
            return problems;
        }
    }
}
