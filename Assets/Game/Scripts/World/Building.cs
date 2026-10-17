using UnityEngine;

namespace Inn
{
    /// <summary>所有可放在网格上的东西的基类</summary>
    public abstract class Building : MonoBehaviour
    {
        public BuildableDef def;
        public Vector2Int cell;   // 左下角格
        public int dir;           // 0 北(+z) 1 东(+x) 2 南 3 西
        public int netId;         // 联机同步用，由主机分配

        protected InnGame Game => InnGame.I;
        /// <summary>联机客户端：不模拟，只按主机快照显示</summary>
        protected bool Replica => Game.Replica;

        /// <summary>联机快照：写入 / 读取本建筑的运行状态（主机写，客户端读）</summary>
        public virtual void WriteState(System.IO.BinaryWriter w) { }
        public virtual void ReadState(System.IO.BinaryReader r) { }
        protected Transform visual;

        public Vector2Int Size => GridMap.Footprint(def, dir);
        public Vector3 Center => new Vector3(cell.x + Size.x * 0.5f, 0, cell.y + Size.y * 0.5f);
        public Vector2Int Forward => GridMap.Dirs[dir];
        public Vector2Int FrontCell => cell + Forward;
        public Vector2Int BackCell => cell - Forward;
        public Zone Zone => Game.grid.ZoneOf(cell);

        public void Init(BuildableDef d, Vector2Int c, int r)
        {
            def = d; cell = c; dir = r;
            name = d.displayName + " " + c;
            transform.position = Center;
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localRotation = Quaternion.Euler(0, dir * 90, 0);
            BuildVisual();
            OnInit();
        }

        protected virtual void OnInit() { }

        protected virtual void BuildVisual()
        {
            var s = def.size;
            Vis.Box(visual, new Vector3(0, def.height / 2, 0), new Vector3(s.x - 0.1f, def.height, s.y - 0.1f), def.color);
        }

        /// <summary>交互提示；返回 null 表示不能交互</summary>
        public virtual string Prompt(Player p) => null;
        public virtual void Interact(Player p) { }
        /// <summary>F 键（灶台开火）</summary>
        public virtual void Use(Player p) { }
        /// <summary>G 键（设定自动菜谱）</summary>
        public virtual void Configure(Player p) { }
        /// <summary>头顶文字（HUD 绘制）</summary>
        public virtual string Label() => null;
        /// <summary>拆除时把里面的东西还给玩家</summary>
        public virtual void OnRemoved(Bag refund) { }

        /// <summary>点到占地矩形的水平距离</summary>
        public float DistanceTo(Vector3 p)
        {
            float dx = Mathf.Max(cell.x - p.x, 0, p.x - (cell.x + Size.x));
            float dz = Mathf.Max(cell.y - p.z, 0, p.z - (cell.y + Size.y));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public Vector3 ClosestPoint(Vector3 p) =>
            new Vector3(Mathf.Clamp(p.x, cell.x, cell.x + Size.x), 0, Mathf.Clamp(p.z, cell.y, cell.y + Size.y));

        /// <summary>以本建筑为中心 3×3 范围（不含自己）里的耕地</summary>
        protected System.Collections.Generic.IEnumerable<Plot> PlotsAround()
        {
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    if ((x != 0 || y != 0) && Game.grid.At(cell + new Vector2Int(x, y)) is Plot p) yield return p;
        }

        protected static string Join(Bag b)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in b.slots) { if (sb.Length > 0) sb.Append(' '); sb.Append(s.item.Name).Append('×').Append(s.count); }
            return sb.ToString();
        }
    }
}
