using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>机关手：每个周期从后方格取 1 件物品放到前方格（只搬前方能接收的东西）</summary>
    public class Inserter : Building
    {
        float timer;
        float swing;     // 0..1 摆臂动画
        Transform arm;
        GameObject held;
        public int moved;

        protected override void BuildVisual()
        {
            Vis.Prim(PrimitiveType.Cylinder, visual, new Vector3(0, 0.1f, 0), new Vector3(0.6f, 0.1f, 0.6f), def.color * 0.8f);
            arm = new GameObject("Arm").transform;
            arm.SetParent(visual, false);
            arm.localPosition = new Vector3(0, 0.25f, 0);
            Vis.Box(arm, new Vector3(0, 0.15f, 0), new Vector3(0.12f, 0.3f, 0.12f), def.color);
            Vis.Box(arm, new Vector3(0, 0.32f, -0.3f), new Vector3(0.1f, 0.08f, 0.65f), new Color(0.85f, 0.6f, 0.25f));
            Vis.Box(arm, new Vector3(0, 0.25f, -0.6f), new Vector3(0.2f, 0.12f, 0.06f), def.color * 0.6f);
        }

        void Update()
        {
            // 臂默认朝后（取货侧），搬运时 180° 甩到前方
            if (swing > 0)
            {
                swing -= Time.deltaTime / Mathf.Max(0.1f, def.interval * 0.8f);
                float a = Mathf.Sin(Mathf.Clamp01(swing) * Mathf.PI) * 180f;
                arm.localRotation = Quaternion.Euler(0, a, 0);
                if (swing <= 0 && held) { Destroy(held); held = null; }
            }
            if (Replica) return;
            timer += Time.deltaTime;
            if (timer < def.interval) return;
            timer = 0;
            var src = Game.grid.At(BackCell) as IItemSource;
            var dst = Game.grid.At(FrontCell) as IItemSink;
            if (src == null || dst == null || ReferenceEquals(src, this)) return;
            Item pick = Item.None;
            foreach (var it in new List<Item>(src.Available()))
                if (dst.CanAccept(it)) { pick = it; break; }
            if (!pick.Valid || !src.Take(pick)) return;
            if (!dst.Accept(pick)) { Game.Toast("机关手：放不进去，物品掉在地上了"); return; }
            moved++;
            swing = 1;
            if (held) Destroy(held);
            held = Vis.ItemModel(arm, pick, 0.8f);
            held.transform.localPosition = new Vector3(0, 0.3f, -0.6f);
        }

        public override string Label() => null;

        public override void WriteState(System.IO.BinaryWriter w) => w.Write(moved);

        public override void ReadState(System.IO.BinaryReader r)
        {
            int m = r.ReadInt32();
            if (m > moved) swing = 1;   // 主机那边搬了一次 → 播放摆臂
            moved = m;
        }
    }
}
