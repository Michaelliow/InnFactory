using UnityEngine;

namespace Inn
{
    public class CropDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public ItemDef seed;
        public ItemDef produce;
        public int seedPrice;
        public float growSeconds;
        public int yield;
        public bool needsWater;
        public Color color = Color.green;
    }
}
