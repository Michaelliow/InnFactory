using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>
    /// 一张连续网格（1 格 = 1 m），沿 x 分成 菜园 | 厨房 | 饭厅 三个区。格 (x,y) 中心在世界 (x+0.5, 0, y+0.5)。
    /// </summary>
    public class GridMap
    {
        public readonly int width, height;
        public readonly int kitchenX, diningX;   // 厨房 / 饭厅起始列
        readonly Building[,] cells;

        public static readonly Vector2Int[] Dirs = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

        public GridMap(GameDatabase db)
        {
            width = db.MapWidth; height = db.mapHeight;
            kitchenX = db.gardenWidth; diningX = db.gardenWidth + db.kitchenWidth;
            cells = new Building[width, height];
        }

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;
        public Building At(Vector2Int c) => InBounds(c) ? cells[c.x, c.y] : null;
        public T At<T>(Vector2Int c) where T : class => At(c) as T;

        public Zone ZoneOf(Vector2Int c)
        {
            if (!InBounds(c)) return Zone.Outside;
            return c.x < kitchenX ? Zone.Garden : c.x < diningX ? Zone.Kitchen : Zone.Dining;
        }

        public Zone ZoneOf(Vector3 world) => ZoneOf(ToCell(world));

        public RectInt ZoneRect(Zone z) => z switch
        {
            Zone.Garden => new RectInt(0, 0, kitchenX, height),
            Zone.Kitchen => new RectInt(kitchenX, 0, diningX - kitchenX, height),
            _ => new RectInt(diningX, 0, width - diningX, height),
        };

        public Vector3 ZoneCenter(Zone z) { var r = ZoneRect(z); return new Vector3(r.center.x, 0, r.center.y); }

        public static Vector3 ToWorld(Vector2Int c) => new Vector3(c.x + 0.5f, 0, c.y + 0.5f);
        public static Vector2Int ToCell(Vector3 w) => new Vector2Int(Mathf.FloorToInt(w.x), Mathf.FloorToInt(w.z));

        public static Vector2Int Footprint(BuildableDef def, int dir) => dir % 2 == 0 ? def.size : new Vector2Int(def.size.y, def.size.x);

        public static IEnumerable<Vector2Int> CellsOf(BuildableDef def, Vector2Int origin, int dir)
        {
            var f = Footprint(def, dir);
            for (int x = 0; x < f.x; x++) for (int y = 0; y < f.y; y++) yield return origin + new Vector2Int(x, y);
        }

        /// <summary>能否放置；不能时 reason 写原因</summary>
        public bool CanPlace(BuildableDef def, Vector2Int origin, int dir, out string reason)
        {
            foreach (var c in CellsOf(def, origin, dir))
            {
                if (!InBounds(c)) { reason = "超出地图"; return false; }
                if (cells[c.x, c.y] != null) { reason = "已被占用"; return false; }
                if (!def.Allowed(ZoneOf(c))) { reason = def.displayName + "不能放在" + Names.Of(ZoneOf(c)); return false; }
            }
            reason = null;
            return true;
        }

        public void Occupy(Building b, bool on)
        {
            foreach (var c in CellsOf(b.def, b.cell, b.dir)) cells[c.x, c.y] = on ? b : null;
        }

        public bool Blocked(Vector3 world)
        {
            var b = At(ToCell(world));
            return b != null && b.def.solid;
        }
    }
}
