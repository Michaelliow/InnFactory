using UnityEngine;

namespace Inn
{
    public class ItemDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public ItemKind kind;
        public DishTag tag;
        public Color color = Color.white;
        public int price;            // 菜品售价 / 生食材参考价
        public CropDef crop;         // 种子对应的作物

        public bool IsDish => kind == ItemKind.Dish;
    }
}
