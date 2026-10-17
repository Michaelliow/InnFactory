using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Inn
{
    /// <summary>
    /// 员工。厨师：按顾客点单做菜（缺中间食材会先做中间食材），从任意储物箱取料，成品放出菜口；
    /// 跑堂：从出菜口取菜送到客人桌上；园丁：收获 / 浇水 / 按上一茬补种（种子从箱子拿），收获物送到最近的箱子。
    /// </summary>
    public class Staff : Walker
    {
        public StaffData data;
        public StaffJob job;
        public string status = "待命";
        public ItemDef cookingFor;                 // 厨师正在做的菜（用于需求统计）
        public readonly List<Item> carry = new List<Item>();
        readonly List<GameObject> carried = new List<GameObject>();
        Coroutine loop;
        Transform hat;

        static readonly Color ChefColor = new Color(0.95f, 0.95f, 0.95f), WaiterColor = new Color(0.3f, 0.6f, 0.45f), GardenerColor = new Color(0.6f, 0.45f, 0.2f);

        public int netId;
        public Vector3 netPos;
        public float netRotY;
        bool replica;

        /// <summary>联机客户端：只建模型，不跑 AI</summary>
        public void SetupReplica(StaffData d, StaffJob j, int id)
        {
            replica = true; data = d; netId = id; name = d.name;
            BuildModel(j);
        }

        public void ApplyReplica(StaffData d, StaffJob j, string st, List<Item> carrying)
        {
            data = d; status = st;
            if (j != job) BuildModel(j);
            if (!carrying.SequenceEqual(carry))
            {
                DropCarryVisuals();
                carry.Clear();
                foreach (var it in carrying) Carry(it);
            }
        }

        void BuildModel(StaffJob j)
        {
            job = j;
            if (model) Destroy(model.gameObject);
            var c = j == StaffJob.Chef ? ChefColor : j == StaffJob.Waiter ? WaiterColor : GardenerColor;
            model = Vis.Chibi(transform, c, Color.Lerp(c, Color.black, 0.4f), 0.95f);
        }

        void Update()
        {
            if (!replica) return;
            var d = netPos - transform.position; d.y = 0;
            float k = 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime);
            if (d.magnitude > 4f) transform.position = netPos; else transform.position += d * k;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, netRotY, 0), k);
        }

        public void Setup(StaffData d, StaffJob j)
        {
            data = d;
            name = d.name;
            speed = d.MoveSpeed;
            SetJob(j);
        }

        public void SetJob(StaffJob j)
        {
            if (loop != null) StopCoroutine(loop);
            Abort();
            BuildModel(j);
            if (j == StaffJob.None) return;
            loop = StartCoroutine(j == StaffJob.Chef ? ChefLoop() : j == StaffJob.Waiter ? WaiterLoop() : GardenerLoop());
        }

        float WorkSeconds(int attr) => 1.0f / (0.5f + attr / 100f);

        // ───────── 搬运 ─────────
        void Carry(Item it)
        {
            carry.Add(it);
            var m = Vis.ItemModel(transform, it, 0.8f);
            m.transform.localPosition = new Vector3(0, 1.45f + 0.15f * carried.Count, 0.1f);
            carried.Add(m);
        }

        void DropCarryVisuals()
        {
            foreach (var g in carried) if (g) Destroy(g);
            carried.Clear();
        }

        Item TakeCarry(int index)
        {
            var it = carry[index];
            carry.RemoveAt(index);
            if (index < carried.Count) { Destroy(carried[index]); carried.RemoveAt(index); }
            return it;
        }

        /// <summary>出错时：放下手里的东西（放进最近的箱子，放不下就进玩家背包），释放占用</summary>
        void Abort()
        {
            foreach (var s in Game.stoves) if (s.reservedBy == this) s.reservedBy = null;
            foreach (var p in Game.plots) if (p.reservedBy == this) p.reservedBy = null;
            foreach (var it in carry)
            {
                var box = Game.NearestContainer(transform.position, it);
                if (box != null) box.bag.Add(it); else Game.player.bag.Add(it);
            }
            carry.Clear();
            DropCarryVisuals();
            cookingFor = null;
        }

        IEnumerator Idle(float s, string why)
        {
            status = why;
            yield return new WaitForSeconds(s);
        }

        IEnumerator Deliver(Item it, bool toPass = true)
        {
            Building dest = null;
            if (toPass && it.def.IsDish) dest = Game.passWindows.OrderBy(p => p.DistanceTo(transform.position)).FirstOrDefault(p => p.CanAccept(it));
            if (dest == null) dest = Game.NearestContainer(transform.position, it);
            if (dest == null) yield break;
            status = "送到" + dest.def.displayName;
            yield return MoveTo(dest);
            int idx = carry.IndexOf(it);
            if (idx < 0) yield break;
            if (dest is IItemSink sink && sink.CanAccept(it)) { sink.Accept(it); TakeCarry(idx); }
        }

        // ───────── 厨师 ─────────
        IEnumerator ChefLoop()
        {
            while (true)
            {
                var recipe = Game.PlanChefStep(out var stove, out var dish);
                if (recipe == null) { yield return Idle(0.5f, "等点单"); continue; }
                stove.reservedBy = this;
                // 做的是客人点的菜 → 出菜口；中间食材（如蛋炒饭要的米饭）→ 放回箱子
                bool forOrder = recipe.output == dish;
                cookingFor = forOrder ? dish : null;
                status = "准备 " + recipe.output.displayName;

                bool ok = true;
                foreach (var ing in recipe.ingredients)
                {
                    var box = Game.FindContainerWith(ing, transform.position);
                    if (box == null) { ok = false; break; }
                    status = "取 " + ing.displayName;
                    yield return MoveTo(box);
                    if (!box.bag.TakeBest(ing, out var it)) { ok = false; break; }
                    Carry(it);
                }
                if (ok)
                {
                    status = "去" + stove.def.displayName;
                    yield return MoveTo(stove);
                    ok = stove != null && !stove.cooking && stove.inputs.Count == 0 && stove.output.IsEmpty && stove.fixedRecipe == null;
                }
                if (!ok) { Abort(); yield return Idle(0.5f, "缺料"); continue; }

                while (carry.Count > 0) stove.AddInput(TakeCarry(0));
                stove.StartCook(this, data.cooking);
                status = "烹饪 " + recipe.output.displayName;
                while (stove != null && stove.cooking) yield return null;
                yield return new WaitForSeconds(0.2f);
                if (stove == null || stove.output.IsEmpty) { Abort(); continue; }
                var result = stove.output.slots[0].item;
                stove.output.Remove(result);
                stove.reservedBy = null;
                Carry(result);
                yield return Deliver(result, forOrder);
                while (carry.Count > 0) { yield return Idle(1f, "放不下了"); yield return Deliver(carry[0], forOrder); }
                cookingFor = null;
            }
        }

        // ───────── 跑堂 ─────────
        IEnumerator WaiterLoop()
        {
            while (true)
            {
                PassWindow pass = null; Customer cust = null; Item dish = Item.None;
                foreach (var p in Game.passWindows)
                {
                    foreach (var s in p.bag.slots)
                    {
                        cust = Game.customers.Where(c => c.state == Customer.State.Waiting && c.Outstanding(s.item.def) > 0 && s.item.q >= c.def.minQuality)
                                             .OrderByDescending(c => c.waited).FirstOrDefault();
                        if (cust != null) { pass = p; dish = s.item; break; }
                    }
                    if (cust != null) break;
                }
                if (cust == null) { yield return Idle(0.4f, "待命"); continue; }

                cust.incoming[dish.def] = (cust.incoming.TryGetValue(dish.def, out var n) ? n : 0) + 1;
                status = "取 " + dish.Name;
                yield return MoveTo(pass);
                bool got = pass != null && pass.Take(dish);
                if (got) Carry(dish);
                if (got && cust != null && cust.seat != null)
                {
                    status = "上菜 " + dish.Name;
                    yield return MoveTo(cust.seat, 0.9f);
                }
                if (cust != null) cust.incoming[dish.def]--;
                if (!got) continue;
                if (cust != null && cust.seat != null && cust.Serve(dish, data.service / 10f, out _)) TakeCarry(carry.IndexOf(dish));
                else yield return Deliver(dish);
                if (carry.Count > 0) Abort();
            }
        }

        // ───────── 园丁 ─────────
        IEnumerator GardenerLoop()
        {
            while (true)
            {
                if (carry.Count >= 6) { yield return DeliverAll(); continue; }
                var here = transform.position;
                var free = Game.plots.Where(p => p.reservedBy == null || p.reservedBy == this).OrderBy(p => p.DistanceTo(here)).ToList();
                var plot = free.FirstOrDefault(p => p.Ripe) ?? free.FirstOrDefault(p => p.NeedsWater);
                Container seedBox = null; Item seed = Item.None;
                if (plot == null)
                {
                    foreach (var p in free.Where(p => p.Empty && p.lastCrop != null))
                    {
                        seedBox = Game.FindContainerWith(p.lastCrop.seed, here);
                        if (seedBox != null) { plot = p; seed = new Item(p.lastCrop.seed); break; }
                    }
                }
                if (plot == null)
                {
                    if (carry.Count > 0) yield return DeliverAll();
                    else yield return Idle(0.5f, "巡视菜园");
                    continue;
                }
                plot.reservedBy = this;
                if (seedBox != null)
                {
                    status = "取种子";
                    yield return MoveTo(seedBox);
                    if (!seedBox.bag.Remove(seed)) { plot.reservedBy = null; continue; }
                }
                status = plot.Ripe ? "收获" : plot.NeedsWater ? "浇水" : "播种";
                yield return MoveTo(plot, 0.6f);
                yield return new WaitForSeconds(WorkSeconds(data.farming));
                if (plot != null)
                {
                    if (plot.Ripe)
                    {
                        plot.Harvest(out var it, out int count);
                        // 种植属性：额外产出概率 种植/300
                        if (Random.value < data.farming / 300f) count++;
                        for (int i = 0; i < count; i++) Carry(it);
                    }
                    else if (plot.NeedsWater) plot.Water(Game.db.waterSeconds);
                    else if (plot.Empty && seed.Valid) plot.Plant(seed.def.crop);
                    else if (seed.Valid) { var b = Game.NearestContainer(transform.position, seed); if (b) b.bag.Add(seed); }
                    plot.reservedBy = null;
                }
            }
        }

        IEnumerator DeliverAll()
        {
            while (carry.Count > 0)
            {
                int before = carry.Count;
                yield return Deliver(carry[0]);
                if (carry.Count == before) { yield return Idle(1f, "箱子满了"); }
            }
        }

        public string Label() => $"{data.name}{Names.Stars(data.stars)}·{Names.Of(job)}：{status}";
    }
}
