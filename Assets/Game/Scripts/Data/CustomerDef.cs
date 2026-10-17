using UnityEngine;

namespace Inn
{
    public class CustomerDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public Vector2Int budget;
        public float patience;
        public Vector2Int orders;
        public DishTag[] likes;
        public int minQuality = 1;
        public int minRepLevel = 1;
        public float weight = 1;
        public Color color = Color.white;
    }
}
