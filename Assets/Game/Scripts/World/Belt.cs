using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>
    /// 传送带：每格最多 1 件物品，沿箭头方向移动。走到尽头时交给下一格传送带，
    /// 或直接塞进前方能接收的建筑（箱子 / 出菜口 / 设了自动菜谱的灶台）。
    /// </summary>
    public class Belt : Building, IItemSource, IItemSink
    {
        public Item item;
        public float progress;   // 0 = 进入边，1 = 出口边
        GameObject itemModel;
        Transform arrow;

        protected override void BuildVisual()
        {
            Vis.Box(visual, new Vector3(0, 0.05f, 0), new Vector3(0.96f, 0.1f, 0.96f), def.color);
            Vis.Box(visual, new Vector3(-0.42f, 0.1f, 0), new Vector3(0.08f, 0.1f, 0.96f), def.color * 0.7f);
            Vis.Box(visual, new Vector3(0.42f, 0.1f, 0), new Vector3(0.08f, 0.1f, 0.96f), def.color * 0.7f);
            arrow = Vis.Box(visual, new Vector3(0, 0.11f, 0), new Vector3(0.22f, 0.02f, 0.22f), new Color(1f, 0.9f, 0.6f)).transform;
            arrow.localRotation = Quaternion.Euler(0, 45, 0);
        }

        void Update()
        {
            // 箭头沿方向滚动，示意流向
            float t = Mathf.Repeat(Time.time * def.speed, 1f);
            arrow.localPosition = new Vector3(0, 0.11f, -0.4f + 0.8f * t);
            if (!item.Valid) return;
            progress += def.speed * Time.deltaTime;
            if (Replica) progress = Mathf.Min(progress, 1f);
            else if (progress >= 1f)
            {
                progress = 1f;
                var next = Game.grid.At(FrontCell);
                if (next is Belt nb)
                {
                    if (!nb.item.Valid && nb.FrontCell != cell) { nb.Receive(item, 0f); Clear(); }
                }
                else if (next is IItemSink sink && sink.CanAccept(item) && sink.Accept(item))
                {
                    Clear();
                }
            }
            if (itemModel)
            {
                var d = new Vector3(Forward.x, 0, Forward.y);
                itemModel.transform.position = Center + d * (progress - 0.5f) + Vector3.up * 0.1f;
            }
        }

        void Receive(Item it, float p)
        {
            item = it; progress = p;
            if (itemModel) Destroy(itemModel);
            itemModel = Vis.ItemModel(transform, it, 0.9f);
            Game.Stats.beltMoves++;
        }

        public override void WriteState(System.IO.BinaryWriter w) { Net.Wire.Write(w, item); w.Write(progress); }

        public override void ReadState(System.IO.BinaryReader r)
        {
            var it = Net.Wire.ReadItem(r);
            float p = r.ReadSingle();
            if (!it.Equals(item)) { if (it.Valid) Receive(it, p); else Clear(); }
            // 客户端自己往前推进做插值，只在差太多时纠正
            else if (Mathf.Abs(progress - p) > 0.35f) progress = p;
        }

        void Clear()
        {
            item = Item.None; progress = 0;
            if (itemModel) Destroy(itemModel);
            itemModel = null;
        }

        public IEnumerable<Item> Available() { if (item.Valid) yield return item; }
        public bool Take(Item it) { if (!item.Valid || !item.Equals(it)) return false; Clear(); return true; }
        public bool CanAccept(Item it) => !item.Valid;
        public bool Accept(Item it) { if (item.Valid) return false; Receive(it, 0.3f); return true; }

        public override string Prompt(Player p)
        {
            var sel = p.Selected;
            if (item.Valid) return $"E 拿起 {item.Name}";
            return sel.Valid ? $"E 放 1 个 {sel.Name} 到传送带" : null;
        }

        public override void Interact(Player p)
        {
            if (item.Valid) { if (p.bag.Add(item) == 1) Clear(); return; }
            var sel = p.Selected;
            if (sel.Valid && p.bag.Remove(sel)) Receive(sel, 0f);
        }

        public override void OnRemoved(Bag refund) { if (item.Valid) refund.Add(item); }
    }
}
