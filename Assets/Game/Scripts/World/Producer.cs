using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>畜舍（鸡舍 / 猪圈 / 鱼塘）：定时产出，满了停产。可被机关手取货。</summary>
    public class Producer : Building, IItemSource
    {
        public int stock;
        float timer;
        readonly List<GameObject> shown = new List<GameObject>();

        protected override void BuildVisual()
        {
            var s = def.size;
            Vis.Box(visual, new Vector3(0, 0.05f, 0), new Vector3(s.x - 0.1f, 0.1f, s.y - 0.1f), def.color * 0.8f);
            Vis.Box(visual, new Vector3(0, 0.45f, 0.35f), new Vector3(s.x * 0.6f, 0.7f, s.y * 0.4f), def.color);
            var roof = Vis.Box(visual, new Vector3(0, 0.9f, 0.35f), new Vector3(s.x * 0.7f, 0.12f, s.y * 0.5f), new Color(0.55f, 0.25f, 0.2f));
            roof.transform.localRotation = Quaternion.Euler(10, 0, 0);
        }

        public override void WriteState(System.IO.BinaryWriter w) { w.Write((byte)stock); w.Write(timer); }

        public override void ReadState(System.IO.BinaryReader r)
        {
            stock = r.ReadByte(); timer = r.ReadSingle();
            if (shown.Count != stock) RefreshStock();
        }

        void Update()
        {
            if (Replica || stock >= def.capacity) return;
            timer += Time.deltaTime;
            if (timer >= def.interval) { timer = 0; stock++; RefreshStock(); }
        }

        void RefreshStock()
        {
            while (shown.Count < stock)
            {
                var m = Vis.ItemModel(visual, new Item(def.produce), 0.9f);
                m.transform.localPosition = new Vector3(-0.6f + shown.Count * 0.3f, 0.1f, -0.5f);
                shown.Add(m);
            }
            while (shown.Count > stock) { Destroy(shown[shown.Count - 1]); shown.RemoveAt(shown.Count - 1); }
        }

        public IEnumerable<Item> Available() { if (stock > 0) yield return new Item(def.produce); }

        public bool Take(Item it)
        {
            if (stock <= 0 || it.def != def.produce) return false;
            stock--; RefreshStock();
            return true;
        }

        public override string Prompt(Player p) => stock > 0 ? $"E 拿取 {def.produce.displayName} ×{stock}" : $"{def.displayName}：下一个 {def.interval - timer:0} 秒";

        public override void Interact(Player p)
        {
            int n = p.bag.Add(new Item(def.produce), stock);
            stock -= n; RefreshStock();
            if (n > 0) Game.Toast($"拿到 {def.produce.displayName} ×{n}");
        }

        public override string Label() => stock > 0 ? $"{def.produce.displayName}×{stock}" : null;
    }
}
