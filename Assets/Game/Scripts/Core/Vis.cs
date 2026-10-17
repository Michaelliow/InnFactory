using System.Collections.Generic;
using UnityEngine;

namespace Inn
{
    /// <summary>原型占位美术：用基础几何体 + 纯色材质拼出所有物体（以后换成正式模型）</summary>
    public static class Vis
    {
        static Material baseMat;
        static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();

        public static void Init(Material m) { baseMat = m; cache.Clear(); }

        public static Material Mat(Color c)
        {
            if (cache.TryGetValue(c, out var m) && m) return m;
            m = baseMat ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.color = c;
            cache[c] = m;
            return m;
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color c, string name = null)
        {
            var go = GameObject.CreatePrimitive(type);
            if (name != null) go.name = name;
            var col = go.GetComponent<Collider>();
            if (col) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(c);
            return go;
        }

        public static GameObject Box(Transform parent, Vector3 localPos, Vector3 scale, Color c, string name = null)
            => Prim(PrimitiveType.Cube, parent, localPos, scale, c, name);

        public static void SetColor(GameObject go, Color c)
        {
            if (go) go.GetComponent<Renderer>().sharedMaterial = Mat(c);
        }

        /// <summary>物品模型：菜品 = 白盘 + 菜；食材 = 小球；种子 = 小袋；黑暗料理 = 冒紫烟的黑团</summary>
        public static GameObject ItemModel(Transform parent, Item it, float scale = 1f)
        {
            var root = new GameObject(it.Name);
            root.transform.SetParent(parent, false);
            var d = it.def;
            if (d == null) return root;
            var t = root.transform;
            switch (d.kind)
            {
                case ItemKind.Dish:
                case ItemKind.Junk:
                    Prim(PrimitiveType.Cylinder, t, new Vector3(0, 0.02f, 0), new Vector3(0.36f, 0.02f, 0.36f) * scale, new Color(0.97f, 0.97f, 0.95f));
                    Prim(PrimitiveType.Sphere, t, new Vector3(0, 0.08f, 0) * scale, new Vector3(0.26f, 0.13f, 0.26f) * scale, d.color);
                    if (it.q >= 2) Prim(PrimitiveType.Sphere, t, new Vector3(0, 0.2f, 0) * scale, Vector3.one * 0.07f * scale, it.q >= 3 ? new Color(1f, 0.6f, 0.1f) : new Color(1f, 0.95f, 0.4f));
                    break;
                case ItemKind.Seed:
                    Box(t, new Vector3(0, 0.1f, 0) * scale, new Vector3(0.2f, 0.2f, 0.14f) * scale, d.color);
                    break;
                default:
                    Prim(PrimitiveType.Sphere, t, new Vector3(0, 0.11f, 0) * scale, Vector3.one * 0.22f * scale, d.color);
                    break;
            }
            return root;
        }

        /// <summary>Q 版两头身小人：身体胶囊 + 大头 + 帽子</summary>
        public static Transform Chibi(Transform parent, Color body, Color hat, float size = 1f)
        {
            var root = new GameObject("Model").transform;
            root.SetParent(parent, false);
            root.localScale = Vector3.one * size;
            Prim(PrimitiveType.Capsule, root, new Vector3(0, 0.35f, 0), new Vector3(0.42f, 0.35f, 0.42f), body);
            Prim(PrimitiveType.Sphere, root, new Vector3(0, 0.95f, 0), Vector3.one * 0.62f, new Color(1f, 0.87f, 0.74f));
            Prim(PrimitiveType.Cylinder, root, new Vector3(0, 1.28f, 0), new Vector3(0.5f, 0.06f, 0.5f), hat);
            Prim(PrimitiveType.Sphere, root, new Vector3(0.12f, 0.98f, 0.27f), Vector3.one * 0.07f, new Color(0.15f, 0.1f, 0.1f));
            Prim(PrimitiveType.Sphere, root, new Vector3(-0.12f, 0.98f, 0.27f), Vector3.one * 0.07f, new Color(0.15f, 0.1f, 0.1f));
            return root;
        }
    }
}
