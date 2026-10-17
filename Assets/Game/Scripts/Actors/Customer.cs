using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Inn
{
    /// <summary>顾客：进门 → 入座 → 点菜 → 等菜 → 吃 → 去柜台结账 → 离开。满意度公式见 GDD 6.3。</summary>
    public class Customer : Walker
    {
        public enum State { Entering, WaitingSeat, ToSeat, Waiting, Eating, ToCounter, Leaving }

        public CustomerDef def;
        public State state;
        public Chair seat;
        public int budget;
        public readonly List<ItemDef> orders = new List<ItemDef>();      // 还没上的菜
        public readonly List<Item> served = new List<Item>();
        public readonly Dictionary<ItemDef, int> incoming = new Dictionary<ItemDef, int>(); // 跑堂正在送的
        public float waited;          // 点菜后等待秒数
        public float satisfaction;
        public float serviceBonus;
        public bool angry;
        readonly List<GameObject> plates = new List<GameObject>();

        public int netId;
        public Vector3 netPos;
        public float netRotY;
        bool replica;

        /// <summary>联机客户端：只建模型，不跑 AI，状态由快照写入</summary>
        public void SetupReplica(CustomerDef d, int id)
        {
            def = d; netId = id; replica = true;
            name = "顾客 " + d.displayName;
            model = Vis.Chibi(transform, d.color, d.color * 0.6f, 0.95f);
        }

        public void ApplyReplica(State s, List<ItemDef> ord, List<Item> srv, float w, Chair st)
        {
            state = s; waited = w; seat = st;
            orders.Clear(); orders.AddRange(ord);
            bool showPlates = (s == State.Waiting || s == State.Eating) && seat != null;
            if (!showPlates) srv = new List<Item>();
            if (srv.Count != plates.Count)
            {
                foreach (var p in plates) if (p) Destroy(p);
                plates.Clear();
                served.Clear(); served.AddRange(srv);
                foreach (var it in srv)
                {
                    var plate = Vis.ItemModel(null, it, 1f);
                    plate.transform.position = seat.DishSpot + new Vector3(0.12f * plates.Count, 0, 0);
                    plates.Add(plate);
                }
            }
        }

        void Update()
        {
            if (!replica) return;
            var d = netPos - transform.position; d.y = 0;
            float k = 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime);
            if (d.magnitude > 4f) transform.position = netPos; else transform.position += d * k;
            transform.position = new Vector3(transform.position.x, netPos.y, transform.position.z);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, netRotY, 0), k);
        }

        public void Setup(CustomerDef d)
        {
            def = d;
            name = "顾客 " + d.displayName;
            budget = Random.Range(d.budget.x, d.budget.y + 1);
            speed = 2.2f;
            model = Vis.Chibi(transform, d.color, d.color * 0.6f, 0.95f);
            StartCoroutine(Life());
        }

        public bool WantsDish(ItemDef d) => orders.Contains(d);
        public int Outstanding(ItemDef d) => orders.Count(o => o == d) - (incoming.TryGetValue(d, out var n) ? n : 0);

        IEnumerator Life()
        {
            state = State.Entering;
            yield return MoveTo(Game.DoorInside, 0.2f);

            // 没座位就在门口等
            state = State.WaitingSeat;
            float wait = 0;
            while ((seat = Game.FindSeat()) == null)
            {
                wait += Time.deltaTime;
                if (wait > Game.db.doorWaitSeconds || !Game.Open)
                {
                    Game.CustomerLeft(this, 0, 0, -1, "没有空位");
                    yield return Leave();
                    yield break;
                }
                yield return null;
            }
            seat.reserved = this;

            state = State.ToSeat;
            yield return MoveTo(seat.Center, 0.05f);
            seat.reserved = null;
            seat.occupant = this;
            transform.rotation = seat.Facing;
            transform.position = seat.Center + Vector3.up * 0.15f;

            ChooseOrders();
            if (orders.Count == 0)
            {
                ReleaseSeat();
                Game.CustomerLeft(this, 0, 0, 0, "没有吃得起的菜");
                yield return Leave();
                yield break;
            }

            state = State.Waiting;
            satisfaction = Game.db.baseSatisfaction + Game.DecorBonus;
            while (orders.Count > 0)
            {
                waited += Time.deltaTime;
                if (waited > def.patience) satisfaction -= Game.db.waitPenaltyPerSec * Time.deltaTime;
                if (satisfaction <= Game.db.angryLeaveAt)
                {
                    angry = true;
                    ReleaseSeat();
                    Game.CustomerLeft(this, 0, 0, -2, "等太久，生气走了");
                    yield return Leave();
                    yield break;
                }
                yield return null;
            }

            // 点的菜全被停售，一道都没上过
            if (served.Count == 0)
            {
                ReleaseSeat();
                Game.CustomerLeft(this, 0, 0, 0, "想吃的菜停售了");
                yield return Leave();
                yield break;
            }

            state = State.Eating;
            yield return new WaitForSeconds(Game.db.eatSeconds);
            foreach (var p in plates) if (p) Destroy(p);
            plates.Clear();

            // 结算满意度
            var db = Game.db;
            satisfaction += (float)served.Average(s => db.qualitySatBonus[Mathf.Clamp(s.q, 0, 3)]);
            if (def.likes != null && served.Any(s => def.likes.Contains(s.def.tag))) satisfaction += db.likeBonus;
            satisfaction += serviceBonus;
            satisfaction = Mathf.Clamp(satisfaction, 0, 100);
            int pay = Mathf.RoundToInt(served.Sum(s => s.def.price * db.qualityPriceMul[Mathf.Clamp(s.q, 0, 3)]));
            int tip = Mathf.RoundToInt(pay * Mathf.Max(0, satisfaction - 60) / 100f);
            int rep = satisfaction < 40 ? -2 : Mathf.RoundToInt(satisfaction / 20f);
            ReleaseSeat();

            state = State.ToCounter;
            var counter = Game.Counter;
            if (counter != null) yield return MoveTo(counter, 0.6f);
            Game.CustomerLeft(this, pay, tip, rep, null);
            yield return Leave();
        }

        void ChooseOrders()
        {
            var menu = Game.MenuDishes().Where(d => d.price > 0).ToList();
            int want = Random.Range(def.orders.x, def.orders.y + 1);
            int left = budget;
            for (int i = 0; i < want; i++)
            {
                var pick = PickDish(menu, left);
                if (pick == null) break;
                orders.Add(pick);
                left -= pick.price;
            }
            if (Game.forcedOrder != null && menu.Contains(Game.forcedOrder))
            {
                orders.Clear();
                orders.Add(Game.forcedOrder);
            }
        }

        /// <summary>预算内按偏好加权随机挑一道（偏好的菜权重 ×3），挑不到返回 null</summary>
        ItemDef PickDish(List<ItemDef> menu, int left)
        {
            var afford = menu.Where(d => d.price <= left).ToList();
            if (afford.Count == 0) return null;
            float r = Random.value * afford.Sum(d => Weight(d));
            foreach (var d in afford) { r -= Weight(d); if (r <= 0) return d; }
            return afford[afford.Count - 1];
        }

        /// <summary>菜品设为停售：还没在送的那几份换成别的在售菜，换不到就取消</summary>
        public void DishWithdrawn(ItemDef d)
        {
            if (replica || state != State.Waiting) return;
            int n = Outstanding(d);
            if (n <= 0) return;
            for (int i = 0; i < n; i++) orders.Remove(d);
            var menu = Game.MenuDishes().Where(m => m.price > 0).ToList();
            int left = budget - served.Sum(s => s.def.price) - orders.Sum(o => o.price);
            for (int i = 0; i < n; i++)
            {
                var pick = PickDish(menu, left);
                if (pick == null) break;
                orders.Add(pick);
                left -= pick.price;
            }
        }

        float Weight(ItemDef d) => def.likes != null && def.likes.Contains(d.tag) ? 3f : 1f;

        void ReleaseSeat()
        {
            if (seat != null) { if (seat.occupant == this) seat.occupant = null; if (seat.reserved == this) seat.reserved = null; }
            seat = null;
        }

        IEnumerator Leave()
        {
            state = State.Leaving;
            yield return MoveTo(Game.DoorInside, 0.2f);
            yield return MoveTo(Game.DoorOutside, 0.2f);
            Game.customers.Remove(this);
            Destroy(gameObject);
        }

        /// <summary>上菜。返回是否成功（不是他点的 / 品质不够会拒绝）</summary>
        public bool Serve(Item dish, float bonus, out string reason)
        {
            reason = null;
            if (state != State.Waiting) { reason = "客人现在不需要上菜"; return false; }
            if (!orders.Contains(dish.def)) { reason = "客人没点这道菜"; return false; }
            if (dish.q < def.minQuality) { reason = $"{def.displayName}只吃 {Names.Stars(def.minQuality)} 以上的菜"; return false; }
            orders.Remove(dish.def);
            served.Add(dish);
            serviceBonus = Mathf.Max(serviceBonus, bonus);
            var plate = Vis.ItemModel(null, dish, 1f);
            plate.transform.position = seat.DishSpot + new Vector3(0.12f * plates.Count, 0, 0);
            plates.Add(plate);
            Game.Stats.dishesServed++;
            return true;
        }

        /// <summary>玩家对着客人按 E：优先用手上选中的菜，否则背包里任意一道他点了的菜</summary>
        public void ServeFrom(Player p)
        {
            if (state != State.Waiting) return;
            var sel = p.Selected;
            Item dish = Item.None;
            if (sel.Valid && orders.Contains(sel.def)) dish = sel;
            else foreach (var s in p.bag.slots) if (orders.Contains(s.item.def) && s.item.q >= def.minQuality) { dish = s.item; break; }
            if (!dish.Valid) { Game.Toast("背包里没有客人点的菜"); return; }
            if (Serve(dish, 0, out var reason)) { p.bag.Remove(dish); Game.Toast($"上菜：{dish.Name}"); }
            else Game.Toast(reason);
        }

        public string Prompt(Player p)
        {
            switch (state)
            {
                case State.Waiting: return $"{def.displayName} 点了：{string.Join("、", orders.Select(o => o.displayName))}　E 上菜";
                case State.Eating: return $"{def.displayName} 正在吃饭";
                default: return null;
            }
        }

        public string Bubble()
        {
            switch (state)
            {
                case State.Waiting:
                    string mood = waited > def.patience ? "（不耐烦）" : "";
                    return $"{string.Join("、", orders.Select(o => o.displayName))}{mood}";
                case State.Eating: return "好吃~";
                case State.WaitingSeat: return "有空位吗？";
                case State.Leaving: return angry ? "哼！" : null;
                default: return null;
            }
        }

        void OnDestroy()
        {
            foreach (var p in plates) if (p) Destroy(p);
            if (!replica) ReleaseSeat();
        }
    }
}
