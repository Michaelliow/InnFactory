using UnityEngine;

namespace Inn
{
    /// <summary>
    /// 掌柜（玩家）。WASD 移动（相对等距镜头），E 交互，F 开火，G 自动菜谱，Q 切换手上物品（含空手），
    /// 1/2/3 去 菜园 / 厨房 / 饭厅。
    /// 联机：每个掌柜一个 Player。本机掌柜 isLocal = true（自己算移动）；其他人的掌柜按网络位置平滑移动。
    /// 交互一律走 InnNet（主机 / 单机直接执行，客户端发给主机执行）。
    /// </summary>
    public class Player : MonoBehaviour
    {
        public static readonly Color[] SlotColors =
        {
            new Color(0.25f, 0.45f, 0.75f), new Color(0.8f, 0.3f, 0.3f), new Color(0.3f, 0.65f, 0.35f),
            new Color(0.75f, 0.55f, 0.2f), new Color(0.55f, 0.35f, 0.7f),
        };

        public Bag bag = new Bag(24, 99);
        public int selected = -1;        // -1 = 空手
        public Building target;          // 当前交互目标
        public int netId = 1;
        public string playerName = "掌柜";
        public bool isLocal = true;
        public int slot;
        public Vector3 netPos;           // 远端掌柜的目标位置
        public float netRotY;
        Transform model;
        float bob;

        InnGame Game => InnGame.I;

        public Item Selected => selected >= 0 && selected < bag.slots.Count ? bag.slots[selected].item : Item.None;

        void Awake()
        {
            bag.Changed += () => { if (selected >= bag.slots.Count) selected = bag.slots.Count - 1; };
            SetSlot(0);
        }

        public void SetSlot(int s)
        {
            slot = s;
            if (model) Destroy(model.gameObject);
            model = Vis.Chibi(transform, SlotColors[s % SlotColors.Length], new Color(0.2f, 0.2f, 0.25f), 1.05f);
        }

        void Update()
        {
            if (!isLocal) { FollowNet(); return; }
            if (Game.ModalOpen) return;
            if (Game.Paused && !Game.Replica) return;
            Move();
            if (InnInput.Down(KeyCode.Alpha1)) GoZone(Zone.Garden);
            if (InnInput.Down(KeyCode.Alpha2)) GoZone(Zone.Kitchen);
            if (InnInput.Down(KeyCode.Alpha3)) GoZone(Zone.Dining);
            if (Game.build.active || Game.panel == InnGame.Panel.Net) { target = null; return; }

            if (InnInput.Down(KeyCode.Q)) Cycle(1);
            target = FindTarget();
            if (target == null) return;
            if (InnInput.Down(KeyCode.E)) Game.net.Interact(this, target, KeyCode.E);
            if (InnInput.Down(KeyCode.F)) Game.net.Interact(this, target, KeyCode.F);
            if (InnInput.Down(KeyCode.G)) Game.net.Interact(this, target, KeyCode.G);
        }

        void FollowNet()
        {
            var d = netPos - transform.position; d.y = 0;
            float k = 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime);
            if (d.magnitude > 4f) transform.position = netPos;
            else transform.position += d * k;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, netRotY, 0), k);
            bob += d.magnitude * k * 6;
            if (model) model.localPosition = new Vector3(0, d.magnitude > 0.02f ? Mathf.Abs(Mathf.Sin(bob)) * 0.08f : 0, 0);
        }

        public void Cycle(int d)
        {
            int n = bag.slots.Count + 1;
            selected = ((selected + 1 + d) % n + n) % n - 1;
        }

        public void Select(ItemDef def) => selected = bag.slots.FindIndex(s => s.item.def == def);
        public void Select(Item it) => selected = bag.slots.FindIndex(s => s.item.Equals(it));

        void Move()
        {
            var m = InnInput.Move;
            if (m.sqrMagnitude < 0.01f) { model.localPosition = Vector3.zero; return; }
            // 等距镜头：屏幕上 = 镜头水平朝向
            var cam = Camera.main.transform;
            var f = cam.forward; f.y = 0; f.Normalize();
            var r = cam.right; r.y = 0; r.Normalize();
            var dir = (f * m.y + r * m.x).normalized;
            // 客户端不跟随主机暂停 / 加速，按真实时间走
            float dt = Game.Replica ? Time.unscaledDeltaTime : Time.deltaTime;
            float dist = Game.db.playerSpeed * dt;
            var pos = transform.position;
            var nx = pos + new Vector3(dir.x * dist, 0, 0);
            if (Walkable(nx)) pos = nx;
            var nz = pos + new Vector3(0, 0, dir.z * dist);
            if (Walkable(nz)) pos = nz;
            transform.position = pos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 15 * dt);
            bob += dist * 6;
            model.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(bob)) * 0.08f, 0);
        }

        bool Walkable(Vector3 p)
        {
            var g = Game.grid;
            if (p.x < 0.2f || p.z < 0.2f || p.x > g.width - 0.2f || p.z > g.height - 0.2f) return false;
            return !g.Blocked(p);
        }

        public void GoZone(Zone z)
        {
            transform.position = Game.ZoneSpawn(z) + new Vector3(0.3f * slot, 0, 0);
            Game.LocalToast("前往" + Names.Of(z));
        }

        /// <summary>范围内、有提示文字的最近建筑；同距离优先面前的</summary>
        Building FindTarget()
        {
            Building best = null;
            float bestScore = float.MaxValue;
            var pos = transform.position;
            var fwd = transform.forward;
            var c = GridMap.ToCell(pos);
            float range = Game.db.interactRange;
            for (int x = -2; x <= 2; x++)
                for (int y = -2; y <= 2; y++)
                {
                    var b = Game.grid.At(c + new Vector2Int(x, y));
                    if (b == null || b == best) continue;
                    float d = b.DistanceTo(pos);
                    if (d > range || b.Prompt(this) == null) continue;
                    var to = b.ClosestPoint(pos) - pos; to.y = 0;
                    float facing = to.sqrMagnitude > 0.001f ? Vector3.Dot(fwd, to.normalized) : 1f;
                    float score = d - facing * 0.6f;
                    if (score < bestScore) { bestScore = score; best = b; }
                }
            return best;
        }
    }
}
