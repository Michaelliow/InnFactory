using System;
using UnityEngine;

namespace Inn
{
    public enum Attr { Cooking, Service, Farming, Stamina }

    /// <summary>NPC 档案（招募榜候选 / 已雇员工共用）。数值规则见 GDD 7.1。</summary>
    [Serializable]
    public class StaffData
    {
        public string name;
        public int stars;
        public Attr main;
        public int cooking, service, farming, stamina;
        public int wage;

        public int Get(Attr a) => a switch { Attr.Cooking => cooking, Attr.Service => service, Attr.Farming => farming, _ => stamina };

        public static string AttrName(Attr a) => a switch { Attr.Cooking => "厨艺", Attr.Service => "服务", Attr.Farming => "种植", _ => "体力" };

        public StaffJob SuggestedJob => main switch { Attr.Cooking => StaffJob.Chef, Attr.Service => StaffJob.Waiter, Attr.Farming => StaffJob.Gardener, _ => StaffJob.Waiter };

        public float MoveSpeed => 2.5f * (0.8f + stamina / 250f);

        public string Summary => $"{name} {Names.Stars(stars)}　厨艺{cooking} 服务{service} 种植{farming} 体力{stamina}　日薪{wage}";

        public static StaffData Roll(GameDatabase db, System.Random rng)
        {
            float total = 0;
            foreach (var r in db.starRows) total += r.weight;
            float pick = (float)rng.NextDouble() * total;
            var row = db.starRows[0];
            foreach (var r in db.starRows) { pick -= r.weight; if (pick <= 0) { row = r; break; } }
            var d = new StaffData
            {
                stars = row.stars,
                wage = row.wage,
                main = (Attr)rng.Next(4),
                name = db.surnames[rng.Next(db.surnames.Length)] + db.givenNames[rng.Next(db.givenNames.Length)],
            };
            int Other() => rng.Next(row.otherRange.x, row.otherRange.y + 1);
            d.cooking = Other(); d.service = Other(); d.farming = Other(); d.stamina = Other();
            int main = rng.Next(row.mainRange.x, row.mainRange.y + 1);
            switch (d.main)
            {
                case Attr.Cooking: d.cooking = main; break;
                case Attr.Service: d.service = main; break;
                case Attr.Farming: d.farming = main; break;
                default: d.stamina = main; break;
            }
            return d;
        }
    }
}
