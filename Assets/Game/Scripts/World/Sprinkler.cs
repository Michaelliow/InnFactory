using UnityEngine;

namespace Inn
{
    /// <summary>洒水器：放下立刻浇一次，之后每个周期给周围 3×3 耕地浇水</summary>
    public class Sprinkler : Building
    {
        float timer;
        Transform ring;
        float pulse;

        protected override void BuildVisual()
        {
            Vis.Prim(PrimitiveType.Cylinder, visual, new Vector3(0, 0.25f, 0), new Vector3(0.15f, 0.25f, 0.15f), def.color * 0.7f);
            Vis.Prim(PrimitiveType.Sphere, visual, new Vector3(0, 0.55f, 0), Vector3.one * 0.25f, def.color);
            ring = Vis.Prim(PrimitiveType.Cylinder, visual, new Vector3(0, 0.15f, 0), new Vector3(0.1f, 0.01f, 0.1f), new Color(0.6f, 0.85f, 1f)).transform;
            ring.gameObject.SetActive(false);
        }

        protected override void OnInit() => timer = def.interval; // 第一帧就浇

        public override void WriteState(System.IO.BinaryWriter w) => w.Write(timer);

        public override void ReadState(System.IO.BinaryReader r)
        {
            float t = r.ReadSingle();
            if (t < timer - 1f) { pulse = 1; ring.gameObject.SetActive(true); }   // 主机那边刚浇过水
            timer = t;
        }

        void Update()
        {
            if (!Replica) timer += Time.deltaTime;
            if (!Replica && timer >= def.interval)
            {
                timer = 0;
                foreach (var p in PlotsAround()) p.Water(def.interval + 2f);
                pulse = 1;
                ring.gameObject.SetActive(true);
            }
            if (pulse > 0)
            {
                pulse -= Time.deltaTime * 1.2f;
                float s = Mathf.Lerp(3f, 0.2f, pulse);
                ring.localScale = new Vector3(s, 0.01f, s);
                if (pulse <= 0) ring.gameObject.SetActive(false);
            }
        }

        public override string Prompt(Player p) => $"洒水器：下次浇水 {def.interval - timer:0} 秒";
    }
}
