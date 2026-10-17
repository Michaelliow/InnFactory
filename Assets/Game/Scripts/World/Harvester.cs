using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>收割机关：收获周围 3×3 的成熟作物存进内部缓存，从前方吐给传送带 / 箱子；机关手也能从它这里取</summary>
    public class Harvester : Building, IItemSource
    {
        public Bag buffer;
        float timer;
        Transform blade;

        protected override void OnInit() => buffer = new Bag(4, def.capacity);

        protected override void BuildVisual()
        {
            Vis.Box(visual, new Vector3(0, 0.3f, 0), new Vector3(0.7f, 0.6f, 0.7f), def.color);
            blade = Vis.Box(visual, new Vector3(0, 0.65f, 0), new Vector3(0.9f, 0.05f, 0.12f), new Color(0.8f, 0.8f, 0.85f)).transform;
            Vis.Box(visual, new Vector3(0, 0.3f, 0.38f), new Vector3(0.3f, 0.2f, 0.1f), new Color(0.3f, 0.2f, 0.1f)); // 出口
        }

        public override void WriteState(System.IO.BinaryWriter w) => Net.Wire.Write(w, buffer);
        public override void ReadState(System.IO.BinaryReader r) => Net.Wire.ReadInto(r, buffer);

        void Update()
        {
            blade.Rotate(0, 360 * Time.deltaTime, 0);
            if (Replica) return;
            timer += Time.deltaTime;
            if (timer >= def.interval)
            {
                timer = 0;
                foreach (var p in PlotsAround())
                {
                    if (!p.Ripe || buffer.Total + p.crop.yield > def.capacity) continue;
                    p.Harvest(out var it, out int n);
                    buffer.Add(it, n);
                    break;
                }
            }
            if (buffer.IsEmpty) return;
            var it0 = buffer.slots[0].item;
            if (Game.grid.At(FrontCell) is IItemSink sink && !(Game.grid.At(FrontCell) is Inserter) && sink.CanAccept(it0) && sink.Accept(it0))
                buffer.Remove(it0);
        }

        public IEnumerable<Item> Available() => new List<Item>(buffer.Distinct());
        public bool Take(Item it) => buffer.Remove(it);

        public override string Prompt(Player p) => buffer.IsEmpty ? "收割机关（等待成熟作物）" : "E 取出 " + Join(buffer);

        public override void Interact(Player p)
        {
            foreach (var s in new List<Slot>(buffer.slots)) buffer.Remove(s.item, p.bag.Add(s.item, s.count));
        }

        public override void OnRemoved(Bag refund) { foreach (var s in buffer.slots) refund.Add(s.item, s.count); }
        public override string Label() => buffer.IsEmpty ? null : Join(buffer);
    }
}
