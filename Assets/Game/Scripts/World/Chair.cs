using UnityEngine;

namespace Inn
{
    /// <summary>椅子：上下左右挨着方桌才算座位。顾客坐在这里，玩家对着椅子按 E 上菜。</summary>
    public class Chair : Building
    {
        public Customer occupant;
        public Customer reserved;

        protected override void BuildVisual()
        {
            Vis.Box(visual, new Vector3(0, 0.4f, 0), new Vector3(0.5f, 0.08f, 0.5f), def.color);
            Vis.Box(visual, new Vector3(0, 0.7f, -0.22f), new Vector3(0.5f, 0.55f, 0.06f), def.color * 0.85f);
            for (int i = 0; i < 4; i++)
                Vis.Box(visual, new Vector3((i % 2 == 0 ? -0.2f : 0.2f), 0.2f, (i < 2 ? -0.2f : 0.2f)), new Vector3(0.06f, 0.4f, 0.06f), def.color * 0.7f);
        }

        /// <summary>相邻的方桌格（优先椅子朝向那一格）</summary>
        public bool TableCell(out Vector2Int tableCell)
        {
            for (int k = 0; k < 4; k++)
            {
                var d = GridMap.Dirs[(dir + k) % 4];
                var b = Game.grid.At(cell + d);
                if (b != null && b.def.kind == BuildKind.Table) { tableCell = cell + d; return true; }
            }
            tableCell = default;
            return false;
        }

        public bool IsSeat => TableCell(out _);
        public bool Vacant => IsSeat && occupant == null && reserved == null;

        /// <summary>菜摆在桌上靠近这把椅子的位置</summary>
        public Vector3 DishSpot
        {
            get
            {
                if (!TableCell(out var t)) return Center + Vector3.up * 0.5f;
                var tw = GridMap.ToWorld(t);
                return Vector3.Lerp(tw, Center, 0.25f) + Vector3.up * 0.75f;
            }
        }

        /// <summary>坐下时面朝桌子</summary>
        public Quaternion Facing
        {
            get
            {
                if (!TableCell(out var t)) return Quaternion.Euler(0, dir * 90, 0);
                var d = GridMap.ToWorld(t) - Center; d.y = 0;
                return Quaternion.LookRotation(d);
            }
        }

        public override string Prompt(Player p) => occupant != null ? occupant.Prompt(p) : IsSeat ? null : "椅子没挨着桌子，不算座位";
        public override void Interact(Player p) { if (occupant != null) occupant.ServeFrom(p); }
        public override string Label() => IsSeat ? null : "无桌";
    }
}
