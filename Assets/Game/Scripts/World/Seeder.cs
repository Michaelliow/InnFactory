using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>播种机关：从后方的箱子取种子，播种周围 3×3 的空耕地（优先种回上一茬的作物）</summary>
    public class Seeder : Building
    {
        float timer;
        Transform hopper;

        protected override void BuildVisual()
        {
            Vis.Box(visual, new Vector3(0, 0.25f, 0), new Vector3(0.7f, 0.5f, 0.7f), def.color);
            hopper = Vis.Box(visual, new Vector3(0, 0.6f, -0.1f), new Vector3(0.5f, 0.25f, 0.4f), new Color(0.6f, 0.45f, 0.25f)).transform;
        }

        void Update()
        {
            if (Replica) return;
            timer += Time.deltaTime;
            if (timer < def.interval) return;
            timer = 0;
            if (!(Game.grid.At(BackCell) is IItemSource src)) return;
            foreach (var p in PlotsAround())
            {
                if (!p.Empty) continue;
                Item seed = Item.None;
                foreach (var it in new List<Item>(src.Available()))
                {
                    if (it.def.kind != ItemKind.Seed) continue;
                    if (!seed.Valid || it.def.crop == p.lastCrop) seed = it;
                }
                if (seed.Valid && src.Take(seed))
                {
                    p.Plant(seed.def.crop);
                    hopper.localScale = new Vector3(0.55f, 0.3f, 0.45f);
                }
                break;
            }
            hopper.localScale = Vector3.Lerp(hopper.localScale, new Vector3(0.5f, 0.25f, 0.4f), 0.5f);
        }

        public override string Prompt(Player p) => Game.grid.At(BackCell) is IItemSource ? "播种机关（从后方箱子取种子）" : "播种机关：后方需要放一个装种子的箱子";
    }
}
