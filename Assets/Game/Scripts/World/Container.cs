using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>储物箱：E 放入当前选中的物品（全部），空手 E 全部取出。机关手 / 传送带可以存取。</summary>
    public class Container : Building, IItemSource, IItemSink
    {
        public Bag bag;

        protected override void OnInit() => bag = new Bag(def.capacity, 50);

        protected override void BuildVisual()
        {
            Vis.Box(visual, new Vector3(0, 0.3f, 0), new Vector3(0.85f, 0.6f, 0.75f), def.color);
            Vis.Box(visual, new Vector3(0, 0.62f, 0), new Vector3(0.9f, 0.06f, 0.8f), def.color * 0.75f);
            Vis.Box(visual, new Vector3(0, 0.35f, 0.38f), new Vector3(0.15f, 0.15f, 0.04f), new Color(0.85f, 0.7f, 0.3f));
        }

        public override void WriteState(System.IO.BinaryWriter w) => Net.Wire.Write(w, bag);
        public override void ReadState(System.IO.BinaryReader r) => Net.Wire.ReadInto(r, bag);

        public IEnumerable<Item> Available() => new List<Item>(bag.Distinct());
        public bool Take(Item it) => bag.Remove(it);
        public bool CanAccept(Item it) => bag.CanAdd(it);
        public bool Accept(Item it) => bag.Add(it) == 1;

        public override string Prompt(Player p)
        {
            var sel = p.Selected;
            string content = bag.IsEmpty ? "空" : Join(bag);
            return sel.Valid ? $"E 放入 {sel.Name} ×{p.bag.Count(sel)}　[{content}]" : $"E 全部取出　[{content}]";
        }

        public override void Interact(Player p)
        {
            var sel = p.Selected;
            if (sel.Valid)
            {
                int n = bag.Add(sel, p.bag.Count(sel));
                p.bag.Remove(sel, n);
                Game.Toast($"放入 {sel.Name} ×{n}");
            }
            else
            {
                foreach (var s in new List<Slot>(bag.slots))
                {
                    int n = p.bag.Add(s.item, s.count);
                    bag.Remove(s.item, n);
                }
            }
        }

        public override void OnRemoved(Bag refund)
        {
            foreach (var s in bag.slots) refund.Add(s.item, s.count);
        }

        public override string Label() => bag.IsEmpty ? null : $"{bag.Total} 件";
    }
}
