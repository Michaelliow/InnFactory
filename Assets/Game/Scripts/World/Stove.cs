using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Inn
{
    /// <summary>
    /// 灶台：放 1~3 个食材 + 本灶台的烹饪方式 → 查菜谱（不分顺序），对不上就是黑暗料理。
    /// 设定了“自动菜谱”（G）后，机关手只会送这道菜需要的食材，凑齐自动开火，产出固定 1★。
    /// </summary>
    public class Stove : Building, IItemSource, IItemSink
    {
        public const int MaxInputs = 3;

        public readonly List<Item> inputs = new List<Item>();
        public Bag output;
        public RecipeDef fixedRecipe;
        public bool cooking;
        public float timer, duration;
        public RecipeDef current;   // null + cooking = 黑暗料理
        public Object cook;         // 正在掌勺的人（玩家 / 员工），自动时为 null
        public Object reservedBy;   // 厨师占用
        int pendingQuality;

        GameObject fire, pot;
        readonly List<GameObject> shown = new List<GameObject>();

        public CookMethod Method => def.method;
        public bool Free => reservedBy == null && !cooking && inputs.Count == 0 && output.IsEmpty && fixedRecipe == null;
        public float Progress01 => cooking ? Mathf.Clamp01(timer / duration) : 0;

        protected override void OnInit() => output = new Bag(1, 5);

        protected override void BuildVisual()
        {
            Vis.Box(visual, new Vector3(0, 0.3f, 0), new Vector3(0.9f, 0.6f, 0.9f), new Color(0.55f, 0.45f, 0.4f));
            pot = Vis.Prim(PrimitiveType.Cylinder, visual, new Vector3(0, 0.7f, 0), new Vector3(0.6f, 0.1f, 0.6f), def.color);
            fire = Vis.Prim(PrimitiveType.Sphere, visual, new Vector3(0, 0.55f, 0.46f), new Vector3(0.3f, 0.15f, 0.06f), new Color(1f, 0.5f, 0.1f));
            fire.SetActive(false);
        }

        void Update()
        {
            if (!Replica && !cooking && fixedRecipe != null && output.IsEmpty && Satisfies(fixedRecipe)) StartCook(null, 0);
            if (!cooking) return;
            timer += Time.deltaTime;
            fire.transform.localScale = new Vector3(0.3f, 0.15f + 0.05f * Mathf.Sin(Time.time * 20), 0.06f);
            pot.transform.localPosition = new Vector3(0, 0.7f + 0.02f * Mathf.Sin(Time.time * 15), 0);
            if (Replica) timer = Mathf.Min(timer, duration);
            else if (timer >= duration) Finish();
        }

        public override void WriteState(System.IO.BinaryWriter w)
        {
            w.Write((byte)inputs.Count);
            foreach (var i in inputs) Net.Wire.Write(w, i);
            Net.Wire.Write(w, output);
            w.Write(cooking); w.Write(timer); w.Write(duration);
            Net.Wire.WriteRecipe(w, current); Net.Wire.WriteRecipe(w, fixedRecipe);
        }

        public override void ReadState(System.IO.BinaryReader r)
        {
            int n = r.ReadByte();
            var ins = new List<Item>();
            for (int i = 0; i < n; i++) ins.Add(Net.Wire.ReadItem(r));
            var outBefore = output.slots.Count > 0 ? output.slots[0].item : Item.None;
            int outCountBefore = output.Total;
            Net.Wire.ReadInto(r, output);
            bool wasCooking = cooking;
            cooking = r.ReadBoolean();
            float t = r.ReadSingle();
            duration = r.ReadSingle();
            if (!cooking || Mathf.Abs(t - timer) > 0.5f) timer = t;
            current = Net.Wire.ReadRecipe(r);
            fixedRecipe = Net.Wire.ReadRecipe(r);
            bool insChanged = !ins.SequenceEqual(inputs);
            if (insChanged) { inputs.Clear(); inputs.AddRange(ins); }
            var outAfter = output.slots.Count > 0 ? output.slots[0].item : Item.None;
            fire.SetActive(cooking);
            if (insChanged || wasCooking != cooking || !outAfter.Equals(outBefore) || output.Total != outCountBefore) RefreshShown();
        }

        public RecipeDef Resolve() => Game.FindRecipe(Method, inputs.Select(i => i.def));

        bool Satisfies(RecipeDef r)
        {
            if (inputs.Count != r.ingredients.Length) return false;
            return Game.FindRecipe(Method, inputs.Select(i => i.def)) == r;
        }

        int Needed(RecipeDef r, ItemDef d) => r.CountOf(d) - inputs.Count(i => i.def == d);

        /// <summary>开火。who = 玩家/员工（决定速度和品质），skill = 厨艺；who 为空表示自动灶台</summary>
        public bool StartCook(Object who, int skill)
        {
            if (cooking || inputs.Count == 0 || !output.IsEmpty) return false;
            current = Resolve();
            float baseSecs = current != null ? current.seconds : 5f;
            duration = who == null ? baseSecs : baseSecs / (0.6f + skill / 100f);
            pendingQuality = who == null ? 1 : RollQuality(skill);
            timer = 0; cooking = true; cook = who;
            fire.SetActive(true);
            RefreshShown();
            return true;
        }

        public static int RollQuality(int skill)
        {
            float p3 = skill > 60 ? (skill - 60) / 200f : 0;
            float p2 = skill / 150f;
            float r = Random.value;
            return r < p3 ? 3 : r < p3 + p2 ? 2 : 1;
        }

        void Finish()
        {
            cooking = false; fire.SetActive(false); cook = null;
            inputs.Clear();
            Item result;
            if (current != null)
            {
                result = new Item(current.output, pendingQuality);
                Game.Discover(current);
            }
            else
            {
                result = new Item(Game.db.darkDish);
                Game.Toast("糟糕，做出了一盘黑暗料理……");
            }
            output.Add(result);
            Game.Stats.cooked++;
            RefreshShown();
        }

        void RefreshShown()
        {
            foreach (var g in shown) Destroy(g);
            shown.Clear();
            for (int i = 0; i < inputs.Count && !cooking; i++)
            {
                var m = Vis.ItemModel(visual, inputs[i], 0.8f);
                m.transform.localPosition = new Vector3(-0.2f + i * 0.2f, 0.76f, 0);
                shown.Add(m);
            }
            if (!output.IsEmpty)
            {
                var m = Vis.ItemModel(visual, output.slots[0].item, 1f);
                m.transform.localPosition = new Vector3(0, 0.78f, 0);
                shown.Add(m);
            }
        }

        public bool AddInput(Item it)
        {
            if (cooking || inputs.Count >= MaxInputs || !output.IsEmpty) return false;
            inputs.Add(it);
            RefreshShown();
            return true;
        }

        // ── 机关手接口 ──
        public IEnumerable<Item> Available() => new List<Item>(output.Distinct());
        public bool Take(Item it) { bool ok = output.Remove(it); if (ok) RefreshShown(); return ok; }
        public bool CanAccept(Item it) => fixedRecipe != null && !cooking && output.IsEmpty && inputs.Count < MaxInputs && Needed(fixedRecipe, it.def) > 0;
        public bool Accept(Item it) => CanAccept(it) && AddInput(it);

        // ── 玩家交互 ──
        public override string Prompt(Player p)
        {
            string head = def.displayName + (fixedRecipe != null ? $"〔自动：{fixedRecipe.output.displayName}〕" : "");
            if (cooking) return $"{head} 烹饪中 {Progress01 * 100:0}%";
            if (!output.IsEmpty) return $"{head}　E 取出 {output.slots[0].item.Name}";
            var sel = p.Selected;
            string ins = inputs.Count == 0 ? "空" : string.Join("+", inputs.Select(i => i.Name));
            string action = sel.Valid && sel.def.kind != ItemKind.Seed && inputs.Count < MaxInputs ? $"E 放入 {sel.Name}" : inputs.Count > 0 ? "E 拿回食材" : "选中食材后按 E 放入";
            string fireHint = inputs.Count > 0 ? "　F 开火（" + Names.Of(Method) + "）" : "";
            return $"{head} [{ins}]　{action}{fireHint}　G 自动菜谱";
        }

        public override void Interact(Player p)
        {
            if (cooking) return;
            if (!output.IsEmpty)
            {
                var s = output.slots[0];
                int n = p.bag.Add(s.item, s.count);
                output.Remove(s.item, n);
                RefreshShown();
                return;
            }
            var sel = p.Selected;
            if (sel.Valid && sel.def.kind != ItemKind.Seed && inputs.Count < MaxInputs)
            {
                if (p.bag.Remove(sel)) AddInput(sel);
                return;
            }
            foreach (var i in inputs) p.bag.Add(i);
            inputs.Clear();
            RefreshShown();
        }

        public override void Use(Player p)
        {
            if (StartCook(p, Game.db.playerCooking))
                Game.Toast($"开火：{string.Join("+", current != null ? current.ingredients.Select(i => i.displayName) : new[] { "？" })}（{Names.Of(Method)}）");
        }

        /// <summary>G：在已发现、且是本灶台烹饪方式的菜谱之间循环（最后一项 = 取消自动）</summary>
        public override void Configure(Player p)
        {
            var list = Game.KnownRecipes().Where(r => r.method == Method).ToList();
            if (list.Count == 0) { Game.Toast("还没有发现这个灶台能做的菜谱"); return; }
            int idx = fixedRecipe == null ? -1 : list.IndexOf(fixedRecipe);
            idx++;
            SetFixed(idx >= list.Count ? null : list[idx]);
            Game.Toast(fixedRecipe == null ? def.displayName + "：取消自动" : $"{def.displayName}：自动做 {fixedRecipe.output.displayName}");
        }

        public void SetFixed(RecipeDef r)
        {
            fixedRecipe = r;
        }

        public override void OnRemoved(Bag refund)
        {
            foreach (var i in inputs) refund.Add(i);
            foreach (var s in output.slots) refund.Add(s.item, s.count);
        }

        public override string Label()
        {
            if (cooking) return (current != null ? current.output.displayName : "？？？") + $" {Progress01 * 100:0}%";
            if (!output.IsEmpty) return output.slots[0].item.Name + " 好了";
            return fixedRecipe != null ? "自动:" + fixedRecipe.output.displayName : null;
        }
    }
}
