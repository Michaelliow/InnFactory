using UnityEngine;

namespace Inn
{
    /// <summary>建造模式（B）：选建筑 → 鼠标所在格预览（绿可放 / 红不可）→ 左键放置，R 旋转，X 切换拆除，右键 / Esc 退出</summary>
    public class BuildController
    {
        readonly InnGame game;
        public bool active;
        public BuildableDef selected;
        public int dir;
        public bool demolish;
        public bool pointerOverUI;     // HUD 每帧写入
        public Vector2Int hoverCell;
        public bool hoverValid;
        public string hoverReason;
        GameObject ghost;
        BuildableDef ghostDef;
        int ghostDir;

        public BuildController(InnGame g) { game = g; }

        public void Toggle()
        {
            active = !active;
            if (!active) { selected = null; demolish = false; DestroyGhost(); }
        }

        public void Select(BuildableDef d) { selected = d; demolish = false; }

        public void Update()
        {
            if (!active) { DestroyGhost(); return; }
            if (InnInput.Down(KeyCode.R)) dir = (dir + 1) % 4;
            if (InnInput.Down(KeyCode.X)) { demolish = !demolish; if (demolish) selected = null; }
            if (Input.GetMouseButtonDown(1)) { if (selected != null || demolish) { selected = null; demolish = false; } else Toggle(); return; }

            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return;
            float t = -ray.origin.y / ray.direction.y;
            hoverCell = GridMap.ToCell(ray.origin + ray.direction * t);

            if (demolish)
            {
                DestroyGhost();
                if (Input.GetMouseButtonDown(0) && !pointerOverUI) game.net.Remove(hoverCell);
                return;
            }
            if (selected == null) { DestroyGhost(); return; }
            hoverValid = game.grid.CanPlace(selected, hoverCell, dir, out hoverReason) && game.gold >= selected.cost;
            if (hoverReason == null && game.gold < selected.cost) hoverReason = "金币不够";
            UpdateGhost();
            if (Input.GetMouseButtonDown(0) && !pointerOverUI) game.net.Place(selected, hoverCell, dir);
        }

        /// <summary>主机直接放置（付费），自动测试用；玩家点击走 InnNet.Place（客户端会发给主机）</summary>
        public Building TryPlace(BuildableDef def, Vector2Int cell, int rot) => game.Place(def, cell, rot, true);

        public bool TryRemove(Vector2Int cell)
        {
            var b = game.grid.At(cell);
            if (b == null) return false;
            int before = game.buildings.Count;
            game.Remove(b);
            return game.buildings.Count < before;
        }

        void UpdateGhost()
        {
            if (ghost == null || ghostDef != selected || ghostDir != dir)
            {
                DestroyGhost();
                ghost = new GameObject("BuildGhost");
                ghostDef = selected; ghostDir = dir;
                var f = GridMap.Footprint(selected, dir);
                Vis.Box(ghost.transform, new Vector3(f.x / 2f, 0.06f, f.y / 2f), new Vector3(f.x, 0.06f, f.y), Color.green, "Base");
                var d = GridMap.Dirs[dir];
                var arrow = Vis.Box(ghost.transform, new Vector3(f.x / 2f + d.x * 0.35f, 0.12f, f.y / 2f + d.y * 0.35f), new Vector3(0.25f, 0.08f, 0.25f), Color.white, "Arrow");
                arrow.transform.localRotation = Quaternion.Euler(0, 45, 0);
            }
            ghost.transform.position = new Vector3(hoverCell.x, 0, hoverCell.y);
            Vis.SetColor(ghost.transform.Find("Base").gameObject, hoverValid ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.95f, 0.3f, 0.25f));
        }

        void DestroyGhost()
        {
            if (ghost) Object.Destroy(ghost);
            ghost = null; ghostDef = null;
        }
    }
}
