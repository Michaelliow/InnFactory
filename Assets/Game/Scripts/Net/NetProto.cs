using System.IO;
using System.Text;
using UnityEngine;

namespace Inn.Net
{
    /// <summary>
    /// 联机协议（版本 <see cref="Version"/>）。主机权威：主机模拟整个客栈，约 15 次/秒给所有人发完整快照；
    /// 客户端只上传自己掌柜的位置（自己算移动，手感不受延迟影响）和操作指令（交互、建造、雇人、买种子……），由主机执行。
    /// </summary>
    public static class NetProto
    {
        public const int Version = 1;
        public const int MaxPlayers = 5;

        public enum Msg : byte
        {
            // 客户端 → 主机
            Hello = 1, Pos = 2, Cmd = 3,
            // 主机 → 客户端
            Welcome = 20, Snapshot = 21, Toast = 22, Full = 23, Reject = 24,
        }

        public enum Cmd : byte
        {
            Interact = 1, Place, Remove, Hire, Fire, SetJob, BuySeed, ToggleDish, RefreshRecruits, NextDay, Pause, Speed,
        }

        public static BinaryWriter Begin(Msg m, out MemoryStream ms)
        {
            ms = new MemoryStream();
            var w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write((byte)m);
            return w;
        }

        public static BinaryReader Open(byte[] data, out Msg m)
        {
            var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            m = (Msg)r.ReadByte();
            return r;
        }

        public static byte[] Full()
        {
            var w = Begin(Msg.Full, out var ms);
            w.Write($"房间已满（最多 {MaxPlayers} 人）");
            return ms.ToArray();
        }

        public static byte[] Hello(string name)
        {
            var w = Begin(Msg.Hello, out var ms);
            w.Write(Version); w.Write(name ?? "");
            return ms.ToArray();
        }

        public static byte[] Pos(Vector3 p, float rotY)
        {
            var w = Begin(Msg.Pos, out var ms);
            w.Write(p.x); w.Write(p.z); w.Write(rotY);
            return ms.ToArray();
        }

        public static byte[] Text(Msg m, string text)
        {
            var w = Begin(m, out var ms);
            w.Write(text ?? "");
            return ms.ToArray();
        }

        public static byte[] Welcome(int playerId)
        {
            var w = Begin(Msg.Welcome, out var ms);
            w.Write(Version); w.Write(playerId);
            return ms.ToArray();
        }
    }

    /// <summary>序列化工具：物品用 GameDatabase 里的下标传（两边数据由同一份生成器生成，下标一致）</summary>
    public static class Wire
    {
        static GameDatabase Db => InnGame.I.db;

        public static int IndexOf<T>(T[] arr, T x) where T : class
        {
            if (x == null) return -1;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == x) return i;
            return -1;
        }

        public static T At<T>(T[] arr, int i) where T : class => i >= 0 && i < arr.Length ? arr[i] : null;

        public static void Write(this BinaryWriter w, Item it)
        {
            w.Write((short)IndexOf(Db.items, it.def));
            w.Write((byte)it.q);
        }

        public static Item ReadItem(this BinaryReader r)
        {
            var def = At(Db.items, r.ReadInt16());
            int q = r.ReadByte();
            return def == null ? Item.None : new Item(def, q);
        }

        public static void Write(this BinaryWriter w, Bag b)
        {
            w.Write((byte)b.slots.Count);
            foreach (var s in b.slots) { w.Write(s.item); w.Write((short)s.count); }
        }

        /// <summary>读背包；内容没变就不动（避免每次快照都触发 Changed 重建模型）</summary>
        public static void ReadInto(this BinaryReader r, Bag b)
        {
            int n = r.ReadByte();
            var items = new Item[n];
            var counts = new int[n];
            for (int i = 0; i < n; i++) { items[i] = r.ReadItem(); counts[i] = r.ReadInt16(); }
            bool same = n == b.slots.Count;
            for (int i = 0; same && i < n; i++) same = b.slots[i].item.Equals(items[i]) && b.slots[i].count == counts[i];
            if (same) return;
            b.slots.Clear();
            for (int i = 0; i < n; i++) if (items[i].Valid) b.slots.Add(new Slot { item = items[i], count = counts[i] });
            b.NotifyChanged();
        }

        public static void WriteDef(this BinaryWriter w, ItemDef d) => w.Write((short)IndexOf(Db.items, d));
        public static ItemDef ReadItemDef(this BinaryReader r) => At(Db.items, r.ReadInt16());
        public static void WriteRecipe(this BinaryWriter w, RecipeDef d) => w.Write((short)IndexOf(Db.recipes, d));
        public static RecipeDef ReadRecipe(this BinaryReader r) => At(Db.recipes, r.ReadInt16());
        public static void WriteCrop(this BinaryWriter w, CropDef d) => w.Write((short)IndexOf(Db.crops, d));
        public static CropDef ReadCrop(this BinaryReader r) => At(Db.crops, r.ReadInt16());

        public static void Write(this BinaryWriter w, StaffData d)
        {
            w.Write(d.name ?? ""); w.Write((byte)d.stars); w.Write((byte)d.main);
            w.Write((byte)d.cooking); w.Write((byte)d.service); w.Write((byte)d.farming); w.Write((byte)d.stamina);
            w.Write(d.wage);
        }

        public static StaffData ReadStaff(this BinaryReader r) => new StaffData
        {
            name = r.ReadString(), stars = r.ReadByte(), main = (Attr)r.ReadByte(),
            cooking = r.ReadByte(), service = r.ReadByte(), farming = r.ReadByte(), stamina = r.ReadByte(),
            wage = r.ReadInt32(),
        };
    }
}
