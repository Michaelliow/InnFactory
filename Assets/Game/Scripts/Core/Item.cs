using System;
using System.Collections.Generic;

namespace Inn
{
    /// <summary>一件物品：定义 + 品质（菜品 1~3★，其余固定 1）</summary>
    public struct Item : IEquatable<Item>
    {
        public ItemDef def;
        public int q;

        public Item(ItemDef def, int q = 1) { this.def = def; this.q = def != null && def.IsDish ? Math.Max(1, q) : 1; }
        public bool Valid => def != null;
        public bool Equals(Item o) => def == o.def && q == o.q;
        public override bool Equals(object o) => o is Item i && Equals(i);
        public override int GetHashCode() => (def ? def.GetInstanceID() : 0) * 7 + q;
        public string Name => def == null ? "—" : def.IsDish && q > 1 ? def.displayName + Names.Stars(q) : def.displayName;
        public static readonly Item None = default;
    }

    public class Slot { public Item item; public int count; }

    /// <summary>通用背包：玩家背包、储物箱、出菜口共用</summary>
    public class Bag
    {
        public readonly List<Slot> slots = new List<Slot>();
        public int maxSlots;
        public int maxStack;
        public event Action Changed;

        public Bag(int maxSlots, int maxStack) { this.maxSlots = maxSlots; this.maxStack = maxStack; }

        public int Total { get { int n = 0; foreach (var s in slots) n += s.count; return n; } }
        public bool IsEmpty => slots.Count == 0;

        public int Count(Item it) { foreach (var s in slots) if (s.item.Equals(it)) return s.count; return 0; }
        public int Count(ItemDef def) { int n = 0; foreach (var s in slots) if (s.item.def == def) n += s.count; return n; }

        public int SpaceFor(Item it)
        {
            foreach (var s in slots) if (s.item.Equals(it)) return maxStack - s.count + Math.Max(0, maxSlots - slots.Count) * maxStack;
            return Math.Max(0, maxSlots - slots.Count) * maxStack;
        }

        public bool CanAdd(Item it, int n = 1) => it.Valid && SpaceFor(it) >= n;

        /// <summary>尽量放入，返回实际放入数量</summary>
        public int Add(Item it, int n = 1)
        {
            if (!it.Valid || n <= 0) return 0;
            int left = n;
            foreach (var s in slots)
                if (s.item.Equals(it) && s.count < maxStack) { int k = Math.Min(left, maxStack - s.count); s.count += k; left -= k; if (left == 0) break; }
            while (left > 0 && slots.Count < maxSlots) { int k = Math.Min(left, maxStack); slots.Add(new Slot { item = it, count = k }); left -= k; }
            if (left != n) Changed?.Invoke();
            return n - left;
        }

        public bool Remove(Item it, int n = 1)
        {
            if (Count(it) < n) return false;
            for (int i = slots.Count - 1; i >= 0 && n > 0; i--)
            {
                var s = slots[i];
                if (!s.item.Equals(it)) continue;
                int k = Math.Min(n, s.count); s.count -= k; n -= k;
                if (s.count == 0) slots.RemoveAt(i);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>取出某种物品中品质最高的一件</summary>
        public bool TakeBest(ItemDef def, out Item taken)
        {
            taken = Item.None;
            foreach (var s in slots) if (s.item.def == def && (!taken.Valid || s.item.q > taken.q)) taken = s.item;
            return taken.Valid && Remove(taken);
        }

        public void Clear() { slots.Clear(); Changed?.Invoke(); }
        public void NotifyChanged() => Changed?.Invoke();

        public IEnumerable<Item> Distinct() { foreach (var s in slots) yield return s.item; }
    }

    public interface IItemSource
    {
        IEnumerable<Item> Available();
        bool Take(Item it);
    }

    public interface IItemSink
    {
        bool CanAccept(Item it);
        bool Accept(Item it);
    }
}
