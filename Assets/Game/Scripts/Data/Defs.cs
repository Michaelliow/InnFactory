using System;
using UnityEngine;

namespace Inn
{
    // 所有数据资源都由 Editor/InnDataFactory.cs 生成，不要手改 .asset。
    // ScriptableObject 每个类单独一个同名文件（Unity 序列化要求）。

    public enum ItemKind { Seed, Raw, Intermediate, Dish, Junk }
    public enum DishTag { None, Staple, Veg, Soup, Cold, Signature }
    public enum CookMethod { None, StirFry, Boil, Steam, Cold, PanFry, Grind, Roast }
    public enum Zone { Garden, Kitchen, Dining, Outside }
    public enum StaffJob { None, Chef, Waiter, Gardener }

    public enum BuildKind
    {
        Plot, Coop, Container, Stove, PassWindow, Table, Chair, Decor, Counter,
        Belt, Inserter, Sprinkler, Harvester, Seeder
    }

    [Flags]
    public enum ZoneMask { Garden = 1, Kitchen = 2, Dining = 4, Any = 7 }

    public static class Names
    {
        public static string Of(CookMethod m) => m switch
        {
            CookMethod.StirFry => "炒", CookMethod.Boil => "煮", CookMethod.Steam => "蒸", CookMethod.Cold => "凉拌",
            CookMethod.PanFry => "煎", CookMethod.Grind => "研磨", CookMethod.Roast => "烤", _ => "—"
        };
        public static string Of(Zone z) => z switch { Zone.Garden => "菜园", Zone.Kitchen => "厨房", Zone.Dining => "饭厅", _ => "门外" };
        public static string Of(StaffJob j) => j switch { StaffJob.Chef => "厨师", StaffJob.Waiter => "跑堂", StaffJob.Gardener => "园丁", _ => "待岗" };
        public static string Stars(int n) => new string('★', Mathf.Clamp(n, 0, 5));
    }

    [Serializable]
    public class StarRow
    {
        public int stars;
        public float weight;
        public Vector2Int mainRange;
        public Vector2Int otherRange;
        public int wage;
    }

    [Serializable]
    public class LayoutEntry
    {
        public BuildableDef def;
        public Vector2Int cell;
        public int dir;
        public ItemDef content; // 耕地预种的种子 / 箱子预放的物品
        public int count;
    }

    [Serializable]
    public class ItemCount { public ItemDef item; public int count; }
}
