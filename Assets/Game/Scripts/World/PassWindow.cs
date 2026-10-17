using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>出菜口 / 传菜台：只放菜品，厨师和传送带把菜放这里，跑堂从这里取菜上桌</summary>
    public class PassWindow : Building, IItemSource, IItemSink
    {
        public Bag bag;
        readonly List<GameObject> shown = new List<GameObject>();

        protected override void OnInit()
        {
            bag = new Bag(def.capacity, def.capacity);
            bag.Changed += RefreshShown;
        }

        protected override void BuildVisual()
        {
            Vis.Box(visual, new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.8f, 0.9f), def.color * 0.7f);
            Vis.Box(visual, new Vector3(0, 0.82f, 0), new Vector3(0.96f, 0.05f, 0.96f), def.color);
        }

        void RefreshShown()
        {
            foreach (var g in shown) Destroy(g);
            shown.Clear();
            int i = 0;
            foreach (var s in bag.slots)
                for (int k = 0; k < s.count; k++, i++)
                {
                    var m = Vis.ItemModel(visual, s.item, 0.7f);
                    m.transform.localPosition = new Vector3(-0.25f + (i % 3) * 0.25f, 0.85f + (i / 3) * 0.1f, -0.2f + (i / 3 % 2) * 0.3f);
                    shown.Add(m);
                }
        }

        public override void WriteState(System.IO.BinaryWriter w) => Net.Wire.Write(w, bag);
        public override void ReadState(System.IO.BinaryReader r) => Net.Wire.ReadInto(r, bag);

        public bool Accepts(ItemDef d) => d != null && (d.kind == ItemKind.Dish || d.kind == ItemKind.Junk);
        public IEnumerable<Item> Available() => new List<Item>(bag.Distinct());
        public bool Take(Item it) => bag.Remove(it);
        public bool CanAccept(Item it) => Accepts(it.def) && bag.Total < def.capacity;
        public bool Accept(Item it) => CanAccept(it) && bag.Add(it) == 1;

        public override string Prompt(Player p)
        {
            var sel = p.Selected;
            string content = bag.IsEmpty ? "空" : Join(bag);
            if (sel.Valid && Accepts(sel.def)) return $"E 放上 {sel.Name}　[{content}]";
            return bag.IsEmpty ? "出菜口（空）" : $"E 取走全部菜品　[{content}]";
        }

        public override void Interact(Player p)
        {
            var sel = p.Selected;
            if (sel.Valid && Accepts(sel.def))
            {
                int n = Mathf.Min(p.bag.Count(sel), def.capacity - bag.Total);
                bag.Add(sel, n); p.bag.Remove(sel, n);
                return;
            }
            foreach (var s in new List<Slot>(bag.slots))
            {
                int n = p.bag.Add(s.item, s.count);
                bag.Remove(s.item, n);
            }
        }

        public override void OnRemoved(Bag refund)
        {
            foreach (var s in bag.slots) refund.Add(s.item, s.count);
        }

        public override string Label() => bag.IsEmpty ? null : Join(bag);
    }
}
