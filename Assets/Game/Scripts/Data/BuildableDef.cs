using UnityEngine;

namespace Inn
{
    public class BuildableDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public BuildKind kind;
        public Vector2Int size = Vector2Int.one;
        public int cost;
        public ZoneMask zones = ZoneMask.Any;
        public Color color = Color.gray;
        public float height = 0.5f;
        public bool solid = true;          // 玩家能否穿过（耕地、传送带可以踩）
        public int decor;                  // 环境分
        public CookMethod method;          // 灶台
        public int capacity;               // 箱子格数 / 出菜口容量
        public float interval;             // 机关周期（秒）
        public float speed;                // 传送带 格/秒
        public ItemDef produce;            // 畜舍产出
        public string blurb;

        public bool Allowed(Zone z) => z != Zone.Outside && (zones & (ZoneMask)(1 << (int)z)) != 0;
    }
}
