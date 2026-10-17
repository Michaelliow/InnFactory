using UnityEngine;

namespace Inn
{
    /// <summary>耕地：播种 → （浇水）→ 成熟 → 收获</summary>
    public class Plot : Building
    {
        public CropDef crop;
        public CropDef lastCrop;
        public float growth;
        public float wateredUntil = -1;
        public Object reservedBy;   // 园丁占用

        GameObject soil, sprout, fruit;

        public bool Empty => crop == null;
        public bool Ripe => crop != null && growth >= crop.growSeconds;
        public bool Watered => Time.time < wateredUntil;
        public bool NeedsWater => crop != null && !Ripe && crop.needsWater && !Watered;
        public float Progress => crop == null ? 0 : Mathf.Clamp01(growth / crop.growSeconds);

        protected override void BuildVisual()
        {
            soil = Vis.Box(visual, new Vector3(0, 0.04f, 0), new Vector3(0.92f, 0.08f, 0.92f), def.color);
            sprout = Vis.Prim(PrimitiveType.Cylinder, visual, new Vector3(0, 0.1f, 0), new Vector3(0.12f, 0.1f, 0.12f), new Color(0.4f, 0.8f, 0.3f));
            fruit = Vis.Prim(PrimitiveType.Sphere, visual, new Vector3(0, 0.45f, 0), Vector3.one * 0.3f, Color.white);
            Refresh();
        }

        public override void WriteState(System.IO.BinaryWriter w)
        {
            Net.Wire.WriteCrop(w, crop); Net.Wire.WriteCrop(w, lastCrop); w.Write(growth); w.Write(Watered);
        }

        public override void ReadState(System.IO.BinaryReader r)
        {
            var c = Net.Wire.ReadCrop(r);
            lastCrop = Net.Wire.ReadCrop(r);
            bool wasRipe = Ripe, changed = c != crop;
            crop = c;
            growth = r.ReadSingle();
            wateredUntil = r.ReadBoolean() ? float.MaxValue : -1;
            if (changed || wasRipe != Ripe) Refresh();
        }

        void Update()
        {
            if (!Replica && crop != null && !Ripe && (!crop.needsWater || Watered))
            {
                growth += Time.deltaTime;
                if (Ripe) Refresh();
            }
            float h = 0.1f + 0.5f * Progress;
            if (crop != null) sprout.transform.localScale = new Vector3(0.12f + 0.15f * Progress, h * 0.5f, 0.12f + 0.15f * Progress);
            sprout.transform.localPosition = new Vector3(0, 0.08f + h * 0.5f, 0);
            Vis.SetColor(soil, Watered ? def.color * 0.6f : def.color);
        }

        void Refresh()
        {
            sprout.SetActive(crop != null);
            fruit.SetActive(Ripe);
            if (crop != null) Vis.SetColor(fruit, crop.color);
        }

        public bool Plant(CropDef c)
        {
            if (!Empty || c == null) return false;
            crop = c; lastCrop = c; growth = 0;
            Refresh();
            return true;
        }

        public void ClearCrop() { crop = null; growth = 0; Refresh(); }

        public void Water(float seconds) { wateredUntil = Mathf.Max(wateredUntil, Time.time + seconds); }

        public bool Harvest(out Item produce, out int count)
        {
            produce = Item.None; count = 0;
            if (!Ripe) return false;
            produce = new Item(crop.produce);
            count = crop.yield;
            crop = null; growth = 0;
            Refresh();
            Game.Stats.harvested += count;
            return true;
        }

        public override string Prompt(Player p)
        {
            if (Ripe) return $"E 收获 {crop.displayName}";
            if (NeedsWater) return $"E 浇水（{crop.displayName} 缺水，停止生长）";
            if (crop != null) return $"{crop.displayName} 生长中 {Progress * 100:0}%";
            var sel = p.Selected;
            if (sel.Valid && sel.def.kind == ItemKind.Seed) return $"E 播种 {sel.def.crop.displayName}";
            return "空地（选中种子后按 E 播种）";
        }

        public override void Interact(Player p)
        {
            if (Ripe)
            {
                Harvest(out var it, out int n);
                int added = p.bag.Add(it, n);
                Game.Toast($"收获 {it.Name} ×{added}");
                return;
            }
            if (NeedsWater) { Water(Game.db.waterSeconds); Game.Toast("浇水"); return; }
            if (Empty)
            {
                var sel = p.Selected;
                if (sel.Valid && sel.def.kind == ItemKind.Seed && p.bag.Remove(sel)) Plant(sel.def.crop);
            }
        }

        public override string Label()
        {
            if (crop == null) return null;
            if (Ripe) return crop.displayName + " 可收获";
            return NeedsWater ? "缺水!" : null;
        }
    }
}
