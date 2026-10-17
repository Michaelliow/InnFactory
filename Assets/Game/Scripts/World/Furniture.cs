using UnityEngine;

namespace Inn
{
    /// <summary>方桌 / 装饰 / 柜台：只有外观和环境分。椅子见 Chair.cs</summary>
    public class Furniture : Building
    {
        protected override void BuildVisual()
        {
            var s = def.size;
            var c = def.color;
            switch (def.kind)
            {
                case BuildKind.Table:
                    Vis.Box(visual, new Vector3(0, def.height, 0), new Vector3(s.x - 0.15f, 0.08f, s.y - 0.15f), c);
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i % 2 == 0 ? -1 : 1) * (s.x / 2f - 0.25f), z = (i < 2 ? -1 : 1) * (s.y / 2f - 0.25f);
                        Vis.Box(visual, new Vector3(x, def.height / 2, z), new Vector3(0.1f, def.height, 0.1f), c * 0.8f);
                    }
                    break;
                case BuildKind.Counter:
                    Vis.Box(visual, new Vector3(0, def.height / 2, 0), new Vector3(s.x - 0.1f, def.height, s.y - 0.3f), c);
                    Vis.Box(visual, new Vector3(0.4f, def.height + 0.12f, 0), new Vector3(0.3f, 0.24f, 0.2f), new Color(0.3f, 0.2f, 0.1f)); // 算盘
                    break;
                case BuildKind.Decor:
                    switch (def.id)
                    {
                        case "plant":
                            Vis.Prim(PrimitiveType.Cylinder, visual, new Vector3(0, 0.15f, 0), new Vector3(0.4f, 0.15f, 0.4f), new Color(0.6f, 0.35f, 0.2f));
                            Vis.Prim(PrimitiveType.Sphere, visual, new Vector3(0, 0.55f, 0), Vector3.one * 0.55f, c);
                            break;
                        case "lantern":
                            Vis.Box(visual, new Vector3(0, 0.6f, 0), new Vector3(0.06f, 1.2f, 0.06f), new Color(0.3f, 0.2f, 0.1f));
                            Vis.Prim(PrimitiveType.Sphere, visual, new Vector3(0, 1.25f, 0), new Vector3(0.4f, 0.5f, 0.4f), c);
                            var l = new GameObject("Light").AddComponent<Light>();
                            l.transform.SetParent(visual, false);
                            l.transform.localPosition = new Vector3(0, 1.3f, 0);
                            l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.35f); l.range = 4; l.intensity = 1.5f;
                            break;
                        case "screen":
                            Vis.Box(visual, new Vector3(0, def.height / 2, 0), new Vector3(s.x - 0.1f, def.height, 0.08f), c);
                            Vis.Box(visual, new Vector3(0, def.height / 2, -0.05f), new Vector3(s.x - 0.4f, def.height - 0.3f, 0.02f), new Color(0.4f, 0.55f, 0.45f));
                            break;
                        case "painting":
                            Vis.Box(visual, new Vector3(0, def.height / 2, 0), new Vector3(0.6f, def.height, 0.06f), c);
                            Vis.Box(visual, new Vector3(0, def.height * 0.6f, -0.04f), new Vector3(0.12f, def.height * 0.5f, 0.02f), new Color(0.1f, 0.1f, 0.1f));
                            break;
                        default: base.BuildVisual(); break;
                    }
                    break;
                default: base.BuildVisual(); break;
            }
        }

        public override string Label() => def.kind == BuildKind.Counter ? "柜台" : null;
    }
}
